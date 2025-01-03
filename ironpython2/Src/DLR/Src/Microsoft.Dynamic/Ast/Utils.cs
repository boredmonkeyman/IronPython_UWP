﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Linq.Expressions;

using System;
using System.Reflection;
using System.Dynamic;
using AnyPrefix.Microsoft.Scripting.Utils;
using AstUtils = AnyPrefix.Microsoft.Scripting.Ast.Utils;

namespace AnyPrefix.Microsoft.Scripting.Ast {
    [Flags]
    public enum ExpressionAccess {
        None = 0,
        Read = 1,
        Write = 2,
        ReadWrite = Read | Write,
    }

    public static partial class Utils {
        /// <summary>
        /// Determines whether specified expression type represents an assignment.
        /// </summary>
        /// <returns>
        /// True if the expression type represents an assignment.
        /// </returns>
        /// <remarks>
        /// Note that some other nodes can also assign to variables, members or array items:
        /// MemberInit, NewArrayInit, Call with ref params, New with ref params, Dynamic with ref params.
        /// </remarks>
        public static bool IsAssignment(this ExpressionType type) {
            return IsWriteOnlyAssignment(type) || IsReadWriteAssignment(type);
        }

        public static bool IsWriteOnlyAssignment(this ExpressionType type) {
            return type == ExpressionType.Assign;
        }

        public static bool IsReadWriteAssignment(this ExpressionType type) {
            switch (type) {
                // unary:
                case ExpressionType.PostDecrementAssign:
                case ExpressionType.PostIncrementAssign:
                case ExpressionType.PreDecrementAssign:
                case ExpressionType.PreIncrementAssign:

                // binary - compound:
                case ExpressionType.AddAssign:
                case ExpressionType.AddAssignChecked:
                case ExpressionType.AndAssign:
                case ExpressionType.DivideAssign:
                case ExpressionType.ExclusiveOrAssign:
                case ExpressionType.LeftShiftAssign:
                case ExpressionType.ModuloAssign:
                case ExpressionType.MultiplyAssign:
                case ExpressionType.MultiplyAssignChecked:
                case ExpressionType.OrAssign:
                case ExpressionType.PowerAssign:
                case ExpressionType.RightShiftAssign:
                case ExpressionType.SubtractAssign:
                case ExpressionType.SubtractAssignChecked:
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Determines if the left child of the given expression is read or written to or both.
        /// </summary>
        public static ExpressionAccess GetLValueAccess(this ExpressionType type) {
            if (type.IsReadWriteAssignment()) {
                return ExpressionAccess.ReadWrite;
            }

            if (type.IsWriteOnlyAssignment()) {
                return ExpressionAccess.Write;
            }

            return ExpressionAccess.Read;
        }

        public static bool IsLValue(this ExpressionType type) {
            // see Expression.RequiresCanWrite
            switch (type) {
                case ExpressionType.Index:
                case ExpressionType.MemberAccess:
                case ExpressionType.Parameter:
                    return true;
            }

            return false;
        }
    }
}
