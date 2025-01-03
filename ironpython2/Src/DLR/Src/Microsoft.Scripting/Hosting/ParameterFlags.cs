﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;

namespace AnyPrefix.Microsoft.Scripting.Hosting {
    /// <summary>
    /// Indications extra information about a parameter such as if it's a parameter array.
    /// </summary>
    [Flags]
    public enum ParameterFlags {
        None,
        ParamsArray = 0x01,
        ParamsDict = 0x02,
    }
}
