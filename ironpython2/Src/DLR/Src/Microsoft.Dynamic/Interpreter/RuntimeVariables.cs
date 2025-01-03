﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Runtime.CompilerServices;

namespace AnyPrefix.Microsoft.Scripting.Interpreter {
    internal sealed class RuntimeVariables : IRuntimeVariables {
        private readonly IStrongBox[] _boxes;

        private RuntimeVariables(IStrongBox[] boxes) {
            _boxes = boxes;
        }

        int IRuntimeVariables.Count => _boxes.Length;

        object IRuntimeVariables.this[int index] {
            get => _boxes[index].Value;
            set => _boxes[index].Value = value;
        }

        internal static IRuntimeVariables Create(IStrongBox[] boxes) {
            return new RuntimeVariables(boxes);
        }
    }
}
