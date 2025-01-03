﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.


using System.Linq.Expressions;

using System;
using System.Reflection;
using AnyPrefix.Microsoft.Scripting.Utils;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using AnyPrefix.Microsoft.Scripting.Interpreter;
using AnyPrefix.Microsoft.Scripting.Runtime;

namespace AnyPrefix.Microsoft.Scripting.Ast {
    /// <summary>
    /// Provides a method call to a method which may return light exceptions. 
    /// 
    /// The call is to a method which supports light exceptions.  When reducing
    /// an additional check and throw is added.  When a block code of is re-written
    /// for light exceptions this instead reduces to not throw a .NET exception.
    /// </summary>
    internal class LightCheckAndThrowExpression : Expression, ILightExceptionAwareExpression {
        private readonly Expression _expr;

        internal LightCheckAndThrowExpression(Expression instance) {
            _expr = instance;
        }

        public override bool CanReduce {
            get { return true; }
        }

        public override ExpressionType NodeType {
            get { return ExpressionType.Extension; }
        }

        public override Type Type {
            get { return _expr.Type; }
        }

        public override Expression Reduce() {
            return Utils.Convert(
                Expression.Call(LightExceptions._checkAndThrow, _expr),
                _expr.Type
            );
        }

        #region ILightExceptionAwareExpression Members

        Expression ILightExceptionAwareExpression.ReduceForLightExceptions() {
            return _expr;
        }

        #endregion

        protected override Expression VisitChildren(ExpressionVisitor visitor) {
            var instance = visitor.Visit(_expr);
            if (instance != _expr) {
                return new LightCheckAndThrowExpression(instance);
            }
            return this;
        }
    }
}
