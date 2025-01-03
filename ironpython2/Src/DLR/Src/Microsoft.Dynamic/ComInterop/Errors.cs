﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

#if FEATURE_COM
using System;

namespace AnyPrefix.Microsoft.Scripting {

    #region Generated Com Exception Factory

    // *** BEGIN GENERATED CODE ***
    // generated by function: gen_expr_factory_com from: generate_exception_factory.py

    /// <summary>
    ///    Strongly-typed and parameterized string factory.
    /// </summary>

    internal static partial class Strings {
        /// <summary>
        /// A string like  "COM object is expected."
        /// </summary>
        internal static string ComObjectExpected {
            get {
                return "COM object is expected.";
            }
        }

        /// <summary>
        /// A string like  "Cannot perform call."
        /// </summary>
        internal static string CannotCall {
            get {
                return "Cannot perform call.";
            }
        }

        /// <summary>
        /// A string like  "COM object does not support events."
        /// </summary>
        internal static string COMObjectDoesNotSupportEvents {
            get {
                return "COM object does not support events.";
            }
        }

        /// <summary>
        /// A string like  "COM object does not support specified source interface."
        /// </summary>
        internal static string COMObjectDoesNotSupportSourceInterface {
            get {
                return "COM object does not support specified source interface.";
            }
        }

        /// <summary>
        /// A string like  "Marshal.SetComObjectData failed."
        /// </summary>
        internal static string SetComObjectDataFailed {
            get {
                return "Marshal.SetComObjectData failed.";
            }
        }

        /// <summary>
        /// A string like  "This method exists only to keep the compiler happy."
        /// </summary>
        internal static string MethodShouldNotBeCalled {
            get {
                return "This method exists only to keep the compiler happy.";
            }
        }

        /// <summary>
        /// A string like  "Unexpected VarEnum {0}."
        /// </summary>
        internal static string UnexpectedVarEnum(object p0) {
            return FormatString("Unexpected VarEnum {0}.", p0);
        }

        /// <summary>
        /// A string like  "Error while invoking {0}."
        /// </summary>
        internal static string DispBadParamCount(object p0) {
            return FormatString("Error while invoking {0}.", p0);
        }

        /// <summary>
        /// A string like  "Error while invoking {0}."
        /// </summary>
        internal static string DispMemberNotFound(object p0) {
            return FormatString("Error while invoking {0}.", p0);
        }

        /// <summary>
        /// A string like  "Error while invoking {0}. Named arguments are not supported."
        /// </summary>
        internal static string DispNoNamedArgs(object p0) {
            return FormatString("Error while invoking {0}. Named arguments are not supported.", p0);
        }

        /// <summary>
        /// A string like  "Error while invoking {0}."
        /// </summary>
        internal static string DispOverflow(object p0) {
            return FormatString("Error while invoking {0}.", p0);
        }

        /// <summary>
        /// A string like  "Could not convert argument {0} for call to {1}."
        /// </summary>
        internal static string DispTypeMismatch(object p0, object p1) {
            return FormatString("Could not convert argument {0} for call to {1}.", p0, p1);
        }

        /// <summary>
        /// A string like  "Error while invoking {0}. A required parameter was omitted."
        /// </summary>
        internal static string DispParamNotOptional(object p0) {
            return FormatString("Error while invoking {0}. A required parameter was omitted.", p0);
        }

        /// <summary>
        /// A string like  "ResolveComReference.CannotRetrieveTypeInformation."
        /// </summary>
        internal static string CannotRetrieveTypeInformation {
            get {
                return "ResolveComReference.CannotRetrieveTypeInformation.";
            }
        }

        /// <summary>
        /// A string like  "IDispatch::GetIDsOfNames behaved unexpectedly for {0}."
        /// </summary>
        internal static string GetIDsOfNamesInvalid(object p0) {
            return FormatString("IDispatch::GetIDsOfNames behaved unexpectedly for {0}.", p0);
        }

        /// <summary>
        /// A string like  "Attempting to wrap an unsupported enum type."
        /// </summary>
        internal static string UnsupportedEnumType {
            get {
                return "Attempting to wrap an unsupported enum type.";
            }
        }

        /// <summary>
        /// A string like  "Attempting to pass an event handler of an unsupported type."
        /// </summary>
        internal static string UnsupportedHandlerType {
            get {
                return "Attempting to pass an event handler of an unsupported type.";
            }
        }

        /// <summary>
        /// A string like  "Could not get dispatch ID for {0} (error: {1})."
        /// </summary>
        internal static string CouldNotGetDispId(object p0, object p1) {
            return FormatString("Could not get dispatch ID for {0} (error: {1}).", p0, p1);
        }

        /// <summary>
        /// A string like  "There are valid conversions from {0} to {1}."
        /// </summary>
        internal static string AmbiguousConversion(object p0, object p1) {
            return FormatString("There are valid conversions from {0} to {1}.", p0, p1);
        }

        /// <summary>
        /// A string like  "Variant.GetAccessor cannot handle {0}."
        /// </summary>
        internal static string VariantGetAccessorNYI(object p0) {
            return FormatString("Variant.GetAccessor cannot handle {0}.", p0);
        }

    }
    /// <summary>
    ///    Strongly-typed and parameterized exception factory.
    /// </summary>

    internal static partial class Error {
        /// <summary>
        /// ArgumentException with message like "COM object does not support events."
        /// </summary>
        internal static Exception COMObjectDoesNotSupportEvents() {
            return new ArgumentException(Strings.COMObjectDoesNotSupportEvents);
        }

        /// <summary>
        /// ArgumentException with message like "COM object does not support specified source interface."
        /// </summary>
        internal static Exception COMObjectDoesNotSupportSourceInterface() {
            return new ArgumentException(Strings.COMObjectDoesNotSupportSourceInterface);
        }

        /// <summary>
        /// InvalidOperationException with message like "Marshal.SetComObjectData failed."
        /// </summary>
        internal static Exception SetComObjectDataFailed() {
            return new InvalidOperationException(Strings.SetComObjectDataFailed);
        }

        /// <summary>
        /// InvalidOperationException with message like "This method exists only to keep the compiler happy."
        /// </summary>
        internal static Exception MethodShouldNotBeCalled() {
            return new InvalidOperationException(Strings.MethodShouldNotBeCalled);
        }

        /// <summary>
        /// InvalidOperationException with message like "Unexpected VarEnum {0}."
        /// </summary>
        internal static Exception UnexpectedVarEnum(object p0) {
            return new InvalidOperationException(Strings.UnexpectedVarEnum(p0));
        }

        /// <summary>
        /// System.Reflection.TargetParameterCountException with message like "Error while invoking {0}."
        /// </summary>
        internal static Exception DispBadParamCount(object p0) {
            return new System.Reflection.TargetParameterCountException(Strings.DispBadParamCount(p0));
        }

        /// <summary>
        /// MissingMemberException with message like "Error while invoking {0}."
        /// </summary>
        internal static Exception DispMemberNotFound(object p0) {
            return new MissingMemberException(Strings.DispMemberNotFound(p0));
        }

        /// <summary>
        /// ArgumentException with message like "Error while invoking {0}. Named arguments are not supported."
        /// </summary>
        internal static Exception DispNoNamedArgs(object p0) {
            return new ArgumentException(Strings.DispNoNamedArgs(p0));
        }

        /// <summary>
        /// OverflowException with message like "Error while invoking {0}."
        /// </summary>
        internal static Exception DispOverflow(object p0) {
            return new OverflowException(Strings.DispOverflow(p0));
        }

        /// <summary>
        /// ArgumentException with message like "Could not convert argument {0} for call to {1}."
        /// </summary>
        internal static Exception DispTypeMismatch(object p0, object p1) {
            return new ArgumentException(Strings.DispTypeMismatch(p0, p1));
        }

        /// <summary>
        /// ArgumentException with message like "Error while invoking {0}. A required parameter was omitted."
        /// </summary>
        internal static Exception DispParamNotOptional(object p0) {
            return new ArgumentException(Strings.DispParamNotOptional(p0));
        }

        /// <summary>
        /// InvalidOperationException with message like "ResolveComReference.CannotRetrieveTypeInformation."
        /// </summary>
        internal static Exception CannotRetrieveTypeInformation() {
            return new InvalidOperationException(Strings.CannotRetrieveTypeInformation);
        }

        /// <summary>
        /// ArgumentException with message like "IDispatch::GetIDsOfNames behaved unexpectedly for {0}."
        /// </summary>
        internal static Exception GetIDsOfNamesInvalid(object p0) {
            return new ArgumentException(Strings.GetIDsOfNamesInvalid(p0));
        }

        /// <summary>
        /// InvalidOperationException with message like "Attempting to wrap an unsupported enum type."
        /// </summary>
        internal static Exception UnsupportedEnumType() {
            return new InvalidOperationException(Strings.UnsupportedEnumType);
        }

        /// <summary>
        /// InvalidOperationException with message like "Attempting to pass an event handler of an unsupported type."
        /// </summary>
        internal static Exception UnsupportedHandlerType() {
            return new InvalidOperationException(Strings.UnsupportedHandlerType);
        }

        /// <summary>
        /// MissingMemberException with message like "Could not get dispatch ID for {0} (error: {1})."
        /// </summary>
        internal static Exception CouldNotGetDispId(object p0, object p1) {
            return new MissingMemberException(Strings.CouldNotGetDispId(p0, p1));
        }

        /// <summary>
        /// System.Reflection.AmbiguousMatchException with message like "There are valid conversions from {0} to {1}."
        /// </summary>
        internal static Exception AmbiguousConversion(object p0, object p1) {
            return new System.Reflection.AmbiguousMatchException(Strings.AmbiguousConversion(p0, p1));
        }

        /// <summary>
        /// NotImplementedException with message like "Variant.GetAccessor cannot handle {0}."
        /// </summary>
        internal static Exception VariantGetAccessorNYI(object p0) {
            return new NotImplementedException(Strings.VariantGetAccessorNYI(p0));
        }

    }

    // *** END GENERATED CODE ***

    #endregion

}
#endif