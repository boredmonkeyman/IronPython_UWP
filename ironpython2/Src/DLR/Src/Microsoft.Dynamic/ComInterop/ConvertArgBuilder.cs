﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#if FEATURE_COM

using System.Linq.Expressions;

using System;

namespace AnyPrefix.Microsoft.Scripting.ComInterop {
    internal class ConvertArgBuilder : SimpleArgBuilder {
        private readonly Type _marshalType;

        internal ConvertArgBuilder(Type parameterType, Type marshalType)
            : base(parameterType) {
            _marshalType = marshalType;
        }

        internal override Expression Marshal(Expression parameter) {
            parameter = base.Marshal(parameter);
            return Expression.Convert(parameter, _marshalType);
        }

        internal override Expression UnmarshalFromRef(Expression newValue) {
            return base.UnmarshalFromRef(Expression.Convert(newValue, ParameterType));
        }
    }
}

#endif
