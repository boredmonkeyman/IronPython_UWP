﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using AnyPrefix.Microsoft.Scripting.Runtime;
using AnyPrefix.Microsoft.Scripting.Utils;
using System.Collections.Generic;

namespace AnyPrefix.Microsoft.Scripting.Debugging {
    public interface ITraceCallback {
        void OnTraceEvent(
            TraceEventKind kind,
            string name,
            string sourceFileName,
            SourceSpan sourceSpan,
            Func<IMyDictionary<object, object>> scopeCallback,
            object payload,
            object customPayload
        );
    }
}
