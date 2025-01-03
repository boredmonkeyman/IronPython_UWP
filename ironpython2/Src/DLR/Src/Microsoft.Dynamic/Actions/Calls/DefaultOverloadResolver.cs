﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using AnyPrefix.Microsoft.Scripting.Actions.Calls;
using AnyPrefix.Microsoft.Scripting.Runtime;
using AnyPrefix.Microsoft.Scripting.Utils;
using AstUtils = AnyPrefix.Microsoft.Scripting.Ast.Utils;

namespace AnyPrefix.Microsoft.Scripting.Actions {
    using Ast = Expression;

    internal sealed class DefaultOverloadResolverFactory : OverloadResolverFactory {
        private readonly DefaultBinder _binder;

        public DefaultOverloadResolverFactory(DefaultBinder binder) {
            Assert.NotNull(binder);
            _binder = binder;
        }

        public override DefaultOverloadResolver CreateOverloadResolver(IList<DynamicMetaObject> args, CallSignature signature, CallTypes callType) {
            return new DefaultOverloadResolver(_binder, args, signature, callType);
        }
    }

    public class DefaultOverloadResolver : OverloadResolver {
        // the first argument is "self" if CallType is ImplicitInstance
        // (TODO: it might be better to change the signature)
        private readonly IList<DynamicMetaObject> _args;
        private readonly CallSignature _signature;
        private readonly CallTypes _callType;
        private DynamicMetaObject _invalidSplattee;
        private static readonly DefaultOverloadResolverFactory _factory = new DefaultOverloadResolverFactory(DefaultBinder.Instance);

        // instance method call:
        public DefaultOverloadResolver(ActionBinder binder, DynamicMetaObject instance, IList<DynamicMetaObject> args, CallSignature signature)
            : this(binder, ArrayUtils.Insert(instance, args), signature, CallTypes.ImplicitInstance) {
        }


        // method call:
        public DefaultOverloadResolver(ActionBinder binder, IList<DynamicMetaObject> args, CallSignature signature)
            : this(binder, args, signature, CallTypes.None) {
        }

        public DefaultOverloadResolver(ActionBinder binder, IList<DynamicMetaObject> args, CallSignature signature, CallTypes callType)
            : base(binder) {
            ContractUtils.RequiresNotNullItems(args, nameof(args));

            Debug.Assert((callType == CallTypes.ImplicitInstance ? 1 : 0) + signature.ArgumentCount == args.Count);
            _args = args;
            _signature = signature;
            _callType = callType;
        }

        public static OverloadResolverFactory Factory => _factory;

        public CallSignature Signature => _signature;

        public IList<DynamicMetaObject> Arguments => _args;

        public CallTypes CallType => _callType;

        protected internal override BitArray MapSpecialParameters(ParameterMapping mapping) {
            //  CallType        call-site   m static                  m instance         m operator/extension
            //  implicit inst.  T.m(a,b)    Ast.Call(null, [a, b])    Ast.Call(a, [b])   Ast.Call(null, [a, b])
            //  none            a.m(b)      Ast.Call(null, [b])       Ast.Call(a, [b])   Ast.Call(null, [a, b])

            if (!mapping.Overload.IsStatic) {
                var type = mapping.Overload.DeclaringType;
                var flags = ParameterBindingFlags.ProhibitNull | (_callType == CallTypes.ImplicitInstance ? ParameterBindingFlags.IsHidden : 0);

                mapping.AddParameter(new ParameterWrapper(null, type, null, flags));
                mapping.AddInstanceBuilder(new InstanceBuilder(mapping.ArgIndex));
            }

            return null;
        }

        protected internal override Candidate CompareEquivalentCandidates(ApplicableCandidate one, ApplicableCandidate two) {
            var result = base.CompareEquivalentCandidates(one, two);
            if (result.Chosen()) {
                return result;
            }

            if (one.Method.Overload.IsStatic && !two.Method.Overload.IsStatic) {
                return _callType == CallTypes.ImplicitInstance ? Candidate.Two : Candidate.One;
            }

            if (!one.Method.Overload.IsStatic && two.Method.Overload.IsStatic) {
                return _callType == CallTypes.ImplicitInstance ? Candidate.One : Candidate.Two;
            }

            return Candidate.Equivalent;
        }

        #region Actual Arguments

        private DynamicMetaObject GetArgument(int i) {
            Debug.Assert(i >= 0);
            return _args[(CallType == CallTypes.ImplicitInstance ? 1 : 0) + i];
        }

        protected override void GetNamedArguments(out IList<DynamicMetaObject> namedArgs, out IList<string> argNames) {
            bool hasNamedArgument = _signature.HasNamedArgument();
            bool hasDictionaryArgument = _signature.HasDictionaryArgument();

            if (hasNamedArgument || hasDictionaryArgument) {
                var objects = new List<DynamicMetaObject>();
                var names = new List<string>();

                if (hasNamedArgument) {
                    for (int i = 0; i < _signature.ArgumentCount; i++) {
                        if (_signature.GetArgumentKind(i) == ArgumentType.Named) {
                            objects.Add(GetArgument(i));
                            names.Add(_signature.GetArgumentName(i));
                        }
                    }
                }

                if (hasDictionaryArgument) {
                    SplatDictionaryArgument(names, objects);
                }

                names.TrimExcess();
                objects.TrimExcess();
                argNames = names;
                namedArgs = objects;
            } else { 
                argNames = ArrayUtils.EmptyStrings;
                namedArgs = DynamicMetaObject.EmptyMetaObjects;
            }
        }

        protected internal override bool AllowByKeywordArgument(OverloadInfo method, ParameterInfo parameter) {
            // params arrays & dictionaries don't allow assignment by keyword
            return base.AllowByKeywordArgument(method, parameter)
                && !parameter.IsParamArray() && !parameter.IsParamDictionary();
        }

        protected override ActualArguments CreateActualArguments(IList<DynamicMetaObject> namedArgs, IList<string> argNames, int preSplatLimit, int postSplatLimit) {
            var res = new List<DynamicMetaObject>();

            if (CallType == CallTypes.ImplicitInstance) {
                res.Add(_args[0]);
            }

            for (int i = 0; i < _signature.ArgumentCount; i++) {
                var arg = GetArgument(i);

                switch (_signature.GetArgumentKind(i)) {
                    case ArgumentType.Simple:
                    case ArgumentType.Instance:
                        res.Add(arg);
                        break;

                    case ArgumentType.List:
                        // TODO: lazy splat
                        IList<object> list = arg.Value as IList<object>;
                        if (list == null) {
                            _invalidSplattee = arg;
                            return null;
                        }

                        for (int j = 0; j < list.Count; j++) {
                            res.Add(
                                DynamicMetaObject.Create(
                                    list[j],
                                    Ast.Call(
                                        Ast.Convert(
                                            arg.Expression,
                                            typeof(IList<object>)
                                        ),
                                        typeof(IList<object>).GetMethod("get_Item"),
                                        AstUtils.Constant(j)
                                    )
                                )
                            );
                        }
                        break;

                    case ArgumentType.Named:
                    case ArgumentType.Dictionary:
                        // already processed
                        break;

                    default:
                        throw new NotImplementedException();
                }
            }

            res.TrimExcess();
            return new ActualArguments(res, namedArgs, argNames, _callType == CallTypes.ImplicitInstance ? 1 : 0,  0, -1, -1);
        }

        private void SplatDictionaryArgument(IList<string> splattedNames, IList<DynamicMetaObject> splattedArgs) {
            Assert.NotNull(splattedNames, splattedArgs);
            Debug.Assert(_signature.HasDictionaryArgument());

            DynamicMetaObject dictMo = GetArgument(_signature.IndexOf(ArgumentType.Dictionary));
            IDictionary dict = (IDictionary)dictMo.Value;
            IDictionaryEnumerator dictEnum = dict.GetEnumerator();
            while (dictEnum.MoveNext()) {
                DictionaryEntry de = dictEnum.Entry;

                string strKey = de.Key is string s ? s : de.Key is Extensible<string> es ? es.Value : null;
                if (strKey != null) {
                    splattedNames.Add(strKey);
                    splattedArgs.Add(
                        DynamicMetaObject.Create(
                            de.Value,
                            Ast.Call(
                                AstUtils.Convert(dictMo.Expression, typeof(IDictionary)),
                                typeof(IDictionary).GetMethod("get_Item"),
                                AstUtils.Constant(strKey)
                            )
                        )
                    );
                }
            }
        }

        protected override Expression GetSplattedExpression() {
            // lazy splatting not used:
            throw Assert.Unreachable;
        }

        protected override object GetSplattedItem(int index) {
            // lazy splatting not used:
            throw Assert.Unreachable;
        }


        #endregion

        public override ErrorInfo MakeInvalidParametersError(BindingTarget target) {
            if (target.Result == BindingResult.InvalidArguments && _invalidSplattee != null) {
                return MakeInvalidSplatteeError(target);
            }
            return base.MakeInvalidParametersError(target);
        }

        private ErrorInfo MakeInvalidSplatteeError(BindingTarget target) {
            return ErrorInfo.FromException(
                Ast.Call(typeof(BinderOps).GetMethod(nameof(BinderOps.InvalidSplatteeError)), 
                    AstUtils.Constant(target.Name),
                    AstUtils.Constant(Binder.GetTypeName(_invalidSplattee.GetLimitType()))
                )
            );
        }
    }
}
