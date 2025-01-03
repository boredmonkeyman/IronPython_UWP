﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#if FEATURE_REFEMIT
using System.Linq.Expressions;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using AnyPrefix.Microsoft.Scripting.Runtime;
using AnyPrefix.Microsoft.Scripting.Utils;
using AstUtils = AnyPrefix.Microsoft.Scripting.Ast.Utils;

namespace AnyPrefix.Microsoft.Scripting.Generation {

    /// <summary>
    /// Serializes constants and dynamic sites so the code can be saved to disk
    /// </summary>
    internal sealed class ToDiskRewriter : DynamicExpressionVisitor {
        private static int _uniqueNameId;
        private List<Expression> _constants;
        private MyDictionary<object, Expression> _constantCache;
        private ParameterExpression _constantPool;
        private MyDictionary<Type, Type> _delegateTypes;
        private int _depth;
        private readonly TypeGen _typeGen;
        
        internal ToDiskRewriter(TypeGen typeGen) {            
            _typeGen = typeGen;
        }

        public LambdaExpression RewriteLambda(LambdaExpression lambda) {
            return (LambdaExpression)Visit(lambda);
        }

        protected override Expression VisitLambda<T>(Expression<T> node) {
            _depth++;
            try {

                // Visit the lambda first, so we walk the tree and find any
                // constants we need to rewrite.
                node = (Expression<T>)base.VisitLambda(node);

                if (_depth != 1) {
                    return node;
                }

                var body = node.Body;

                if (_constants != null) {
                    // Rewrite the constants, they can contain embedded
                    // CodeContextExpressions
                    for (int i = 0; i < _constants.Count; i++) {
                        _constants[i] = Visit(_constants[i]);
                    }

                    // Add the consant pool variable to the top lambda
                    // We first create the array and then assign into it so that we can refer to the
                    // array and read values out that have already been created.
                    ReadOnlyCollectionBuilder<Expression> assigns = new ReadOnlyCollectionBuilder<Expression>(_constants.Count + 2);
                    assigns.Add(Expression.Assign(
                        _constantPool,
                        Expression.NewArrayBounds(typeof(object), Expression.Constant(_constants.Count))
                    ));

                    // emit inner most constants first so they're available for outer most constants to consume
                    for (int i = _constants.Count - 1; i >= 0 ; i--) {
                        assigns.Add(
                            Expression.Assign(
                                Expression.ArrayAccess(_constantPool, Expression.Constant(i)),
                                _constants[i]
                            )
                        );
                    }
                    assigns.Add(body);

                    body = Expression.Block(new[] { _constantPool }, assigns);
                }

                // Rewrite the lambda
                return Expression.Lambda<T>(
                    body,
                    node.Name + "$" + Interlocked.Increment(ref _uniqueNameId),
                    node.TailCall,
                    node.Parameters
                );

            } finally {
                _depth--;
            }
        }

        protected override Expression VisitExtension(Expression node) {
            if (node.NodeType == ExpressionType.Dynamic) {
                // the node was dynamic, the dynamic nodes were removed,
                // we now need to rewrite any call sites.
                return VisitDynamic((DynamicExpression)node);
            }

            return Visit(node.Reduce());
        }

        protected override Expression VisitConstant(ConstantExpression node) {
            if (node.Value is CallSite site) {
                return RewriteCallSite(site);
            }

            if (node.Value is IExpressionSerializable exprSerializable) {
                EnsureConstantPool();

                if (!_constantCache.TryGetValue(node.Value, out Expression res)) {
                    Expression serialized = exprSerializable.CreateExpression();
                    _constants.Add(serialized);

                    _constantCache[node.Value] = res = AstUtils.Convert(
                        Expression.ArrayAccess(_constantPool, AstUtils.Constant(_constants.Count - 1)),
                        serialized.Type
                    );
                }

                return res;
            }

            if (node.Value is string[] strings) {
                if (strings.Length == 0) {
                    return Expression.Field(null, typeof(ArrayUtils).GetDeclaredField("EmptyStrings"));
                }

                _constants.Add(
                    Expression.NewArrayInit(
                         typeof(string),
                         new ReadOnlyCollection<Expression>(
                             strings.Map(s => Expression.Constant(s, typeof(string)))
                         )
                     )
                 );

                return AstUtils.Convert(
                    Expression.ArrayAccess(_constantPool, AstUtils.Constant(_constants.Count - 1)),
                    typeof(string[])
                );
            }

            return base.VisitConstant(node);
        }

        // If the DynamicExpression uses a transient (in-memory) type for its
        // delegate, we need to replace it with a new delegate type that can be
        // saved to disk
        protected override Expression VisitDynamic(DynamicExpression node) {
            if (RewriteDelegate(node.DelegateType, out Type delegateType)) {
                node = DynamicExpression.MakeDynamic(delegateType, node.Binder, node.Arguments);
            }

            // Reduce dynamic expression so that the lambda can be emitted as a non-dynamic method.
            return Visit(CompilerHelpers.Reduce(node));
        }

        private bool RewriteDelegate(Type delegateType, out Type newDelegateType) {
            if (!ShouldRewriteDelegate(delegateType)) {
                newDelegateType = null;
                return false;
            }

            if (_delegateTypes == null) {
                _delegateTypes = new MyDictionary<Type, Type>();
            }

            // TODO: should caching move to AssemblyGen?
            if (!_delegateTypes.TryGetValue(delegateType, out newDelegateType)) {
                MethodInfo invoke = delegateType.GetMethod("Invoke");

                newDelegateType = _typeGen.AssemblyGen.MakeDelegateType(
                    delegateType.Name,
                    invoke.GetParameters().Map(p => p.ParameterType),
                    invoke.ReturnType
                );

                _delegateTypes[delegateType] = newDelegateType;
            }

            return true;
        }

        private bool ShouldRewriteDelegate(Type delegateType) {
            // We need to replace a transient delegateType with one stored in
            // the assembly we're saving to disk.
            //
            // When the delegateType is a function that accepts 14 or more parameters,
            // it is transient, but it is an InternalModuleBuilder, not a ModuleBuilder
            // so some reflection is done here to determine if a delegateType is an
            // InternalModuleBuilder and if it is, if it is transient.
            //
            // One complication:
            // SaveAssemblies mode prevents us from detecting the module as
            // transient. If that option is turned on, always replace delegates
            // that live in another AssemblyBuilder
#if FEATURE_REFEMIT_FULL
            var module = delegateType.Module as ModuleBuilder;

            if (module == null) {
                if (delegateType.Module.GetReleaseType() == typeof(ModuleBuilder).Assembly.GetType("System.Reflection.Emit.InternalModuleBuilder")) {
                    if ((bool)delegateType.Module.GetReleaseType().InvokeMember("IsTransientInternal", BindingFlags.InvokeMethod | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, delegateType.Module, null)) {
                        return true;
                    }
                }
                else {
                    return false;
                }
            }
            else if (module.IsTransient()) {
                return true;
            }

            if (Snippets.Shared.SaveSnippets && module.Assembly != _typeGen.AssemblyGen.AssemblyBuilder) {
                return true;
            }

            return false;
#else
            return true; // TODO:
#endif
        }

        private Expression RewriteCallSite(CallSite site) {
            IExpressionSerializable serializer = site.Binder as IExpressionSerializable;
            if (serializer == null) {
                throw Error.GenNonSerializableBinder();
            }

            EnsureConstantPool();

            Type siteType = site.GetReleaseType();

            _constants.Add(Expression.Call(siteType.GetMethod("Create"), serializer.CreateExpression()));

            // rewrite the node...
            return Visit(
                AstUtils.Convert(
                    Expression.ArrayAccess(_constantPool, AstUtils.Constant(_constants.Count - 1)),
                    siteType
                )
            );
        }

        private void EnsureConstantPool() {
            // add the initialization code that we'll generate later into the outermost
            // lambda and then return an index into the array we'll be creating.
            if (_constantPool == null) {
                _constantPool = Expression.Variable(typeof(object[]), "$constantPool");
                _constants = new List<Expression>();
                _constantCache = new MyDictionary<object, Expression>();
            }
        }
    }
}
#endif
