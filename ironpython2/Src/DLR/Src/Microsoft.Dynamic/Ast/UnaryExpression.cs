﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Linq.Expressions;

using System;
using AnyPrefix.Microsoft.Scripting.Runtime;
using AnyPrefix.Microsoft.Scripting.Utils;
using System.Reflection;

namespace AnyPrefix.Microsoft.Scripting.Ast {
    public static partial class Utils {
        /// <summary>
        /// Converts an expression to a void type.
        /// </summary>
        /// <param name="expression">An <see cref="Expression"/> to convert to void. </param>
        /// <returns>An <see cref="Expression" /> that has the <see cref="P:System.Linq.Expressions.Expression.NodeType" /> property equal to <see cref="F:System.Linq.Expressions.ExpressionType.ConvertChecked" /> and the <see cref="P:System.Linq.Expressions.UnaryExpression.Operand" /> and <see cref="P:System.Linq.Expressions.Expression.Type" /> property set to void.</returns>
        public static Expression Void(Expression expression) {
            ContractUtils.RequiresNotNull(expression, nameof(expression));
            if (expression.Type == typeof(void)) {
                return expression;
            }
            return Expression.Block(expression, Empty());
        }

        public static Expression Convert(Expression expression, Type type) {
            ContractUtils.RequiresNotNull(expression, nameof(expression));

            if (expression.Type == type) {
                return expression;
            }

            if (expression.Type == typeof(void)) {
                return Expression.Block(expression, Default(type));
            }

            if (type == typeof(void)) {
                return Void(expression);
            }

            // TODO: this is not the right level for this to be at. It should
            // be pushed into languages if they really want this behavior.
            if (type == typeof(object)) {
                return Box(expression);
            }

            return Expression.Convert(expression, type);
        }

        /// <summary>
        /// Returns an expression that boxes a given value. Uses boxed objects cache for Int32 and Boolean types.
        /// </summary>
        public static Expression Box(Expression expression) {
            MethodInfo m;
            if (expression.Type == typeof(int)) {
                m = ScriptingRuntimeHelpers.Int32ToObjectMethod;
            } else if (expression.Type == typeof(bool)) {
                m = ScriptingRuntimeHelpers.BooleanToObjectMethod;
            } else {
                m = null;
            }

            return Expression.Convert(expression, typeof(object), m);
        }
    }
}
