﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Dynamic;
using System.Reflection;
using System.Runtime.CompilerServices;

using System.Linq.Expressions;

using AnyPrefix.Microsoft.Scripting.Actions;
using AnyPrefix.Microsoft.Scripting.Generation;
using AnyPrefix.Microsoft.Scripting.Runtime;
using IronPython.Runtime.Operations;

namespace IronPython.Runtime.Binding {

    partial class BinaryRetTypeBinder : ComboBinder, IExpressionSerializable {
        private readonly DynamicMetaObjectBinder _opBinder;
        private readonly PythonConversionBinder _convBinder;

        public BinaryRetTypeBinder(DynamicMetaObjectBinder operationBinder, PythonConversionBinder conversionBinder) :
            base(new BinderMappingInfo(
                    operationBinder,
                    ParameterMappingInfo.Parameter(0),
                    ParameterMappingInfo.Parameter(1)
                ),
                new BinderMappingInfo(
                    conversionBinder,
                    ParameterMappingInfo.Action(0)
                )
            ) {
            _opBinder = operationBinder;
            _convBinder = conversionBinder;
        }

        public override Type ReturnType {
            get {
                return _convBinder.Type;
            }
        }

        #region IExpressionSerializable Members

        public Expression CreateExpression() {
            
            return Expression.Call(
                typeof(PythonOps).GetMethod(nameof(PythonOps.MakeComboAction)),
                BindingHelpers.CreateBinderStateExpression(),
                ((IExpressionSerializable)_opBinder).CreateExpression(),
                _convBinder.CreateExpression()
            );
        }

        #endregion
    }
}
