﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Linq.Expressions;

using System;
using System.Dynamic;
using System.Reflection;
using AnyPrefix.Microsoft.Scripting.Actions;
using AnyPrefix.Microsoft.Scripting.Generation;
using AnyPrefix.Microsoft.Scripting.Utils;

using IronPython.Runtime.Operations;

using AstUtils = AnyPrefix.Microsoft.Scripting.Ast.Utils;

namespace IronPython.Runtime.Binding {
    using Ast = Expression;

    partial class PythonBinder : DefaultBinder {
        public DynamicMetaObject Create(CallSignature signature, DynamicMetaObject target, DynamicMetaObject[] args, Expression contextExpression) {

            Type t = GetTargetType(target.Value);

            if (t != null) {

                if (typeof(Delegate).IsAssignableFrom(t) && args.Length == 1) {
                    // PythonOps.GetDelegate(CodeContext context, object callable, Type t);
                    return new DynamicMetaObject(
                        Ast.Call(
                            typeof(PythonOps).GetMethod(nameof(PythonOps.GetDelegate)),
                            contextExpression,
                            AstUtils.Convert(args[0].Expression, typeof(object)),
                            Expression.Constant(t)
                        ),
                        target.Restrictions.Merge(BindingRestrictions.GetInstanceRestriction(target.Expression, target.Value))
                    );
                }

                return CallMethod(
                    new PythonOverloadResolver(
                        this,
                        args,
                        signature,
                        contextExpression
                    ),
                    PythonTypeOps.GetConstructors(t, PrivateBinding),
                    target.Restrictions.Merge(BindingRestrictions.GetInstanceRestriction(target.Expression, target.Value))
                );
            }

            return null;
        }

        private static Type GetTargetType(object target) {
            if (target is TypeTracker tt) {
                return tt.Type;
            }
            return target as Type;
        }
    }
}
