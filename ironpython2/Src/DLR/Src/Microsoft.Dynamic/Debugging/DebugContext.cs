﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Threading;

using AnyPrefix.Microsoft.Scripting.Utils;

namespace AnyPrefix.Microsoft.Scripting.Debugging.CompilerServices {

    /// <summary>
    /// Provides services to compilers for instrumenting code with tracebacks.
    /// </summary>
    public sealed partial class DebugContext {
        private IDebugCallback _traceHook;
        private DebugMode _debugMode;
        private readonly ThreadLocal<DebugThread> _thread;
        private DebugThread _cachedThread;
        private readonly MyDictionary<string, DebugSourceFile> _sourceFiles;
        private readonly IDebugThreadFactory _threadFactory;

        private DebugContext(IDebugThreadFactory runtimeThreadFactory) {
            _thread = new ThreadLocal<DebugThread>();
            _sourceFiles = new MyDictionary<string, DebugSourceFile>(StringComparer.OrdinalIgnoreCase);
            _threadFactory = runtimeThreadFactory;
        }

        #region Public members

        /// <summary>
        /// Creates a new instance of DebugContext
        /// </summary>
        public static DebugContext CreateInstance() {
            return new DebugContext(new DefaultDebugThreadFactory());
        }

        internal static DebugContext CreateInstance(IDebugThreadFactory runtimeThreadFactory) {
            return new DebugContext(runtimeThreadFactory);
        }

        /// <summary>
        /// Transforms a LambdaExpression to a debuggable LambdaExpression
        /// </summary>
        public LambdaExpression TransformLambda(LambdaExpression lambda, DebugLambdaInfo lambdaInfo) {
            ContractUtils.RequiresNotNull(lambda, nameof(lambda));
            ContractUtils.RequiresNotNull(lambdaInfo, nameof(lambdaInfo));

            return new DebuggableLambdaBuilder(this, lambdaInfo).Transform(lambda);
        }

        /// <summary>
        /// Transforms a LambdaExpression to a debuggable LambdaExpression
        /// </summary>
        public LambdaExpression TransformLambda(LambdaExpression lambda) {
            ContractUtils.RequiresNotNull(lambda, nameof(lambda));
            return new DebuggableLambdaBuilder(this, new DebugLambdaInfo(null, null, false, null, null, null)).Transform(lambda);
        }

        /// <summary>
        /// Resets a state associated with a source file that's maintained in the DebugContext
        /// </summary>
        public void ResetSourceFile(string sourceFileName) {
            ContractUtils.RequiresNotNull(sourceFileName, nameof(sourceFileName));
            _sourceFiles.Remove(sourceFileName);
        }

        [Obsolete("do not call this property", true)]
        public int Mode {
            get { return (int)_debugMode; }
        }

        #endregion

        internal DebugMode DebugMode {
            get { return _debugMode; }
            set {
                _debugMode = value;

                // Also update debug mode for all source files
                foreach (DebugSourceFile file in _sourceFiles.Values) {
                    file.DebugMode = value;
                }
            }
        }

        internal DebugSourceFile Lookup(string sourceFile) {
            if (_sourceFiles.TryGetValue(sourceFile, out DebugSourceFile debugSourceFile)) {
                return debugSourceFile;
            }

            return null;
        }

        /// <summary>
        /// Threads
        /// </summary>
        internal IEnumerable<DebugThread> Threads {
            // $TODO: only return the threads that are in break mode
            get {
                foreach (var thread in _thread.Values)
                    if (thread != null && thread.FrameCount > 0)
                        yield return thread;
            }
        }

        /// <summary>
        /// Hook
        /// </summary>
        internal IDebugCallback DebugCallback {
            get { return _traceHook; }
            set { _traceHook = value; }
        }

        internal DebugSourceFile GetDebugSourceFile(string sourceFile) {
            DebugSourceFile file;
            lock (((ICollection)_sourceFiles).SyncRoot) {
                if (!_sourceFiles.TryGetValue(sourceFile, out file)) {
                    file = new DebugSourceFile(sourceFile, _debugMode);
                    _sourceFiles.Add(sourceFile, file);
                }
            }

            return file;
        }

        internal static FunctionInfo CreateFunctionInfo(
            Delegate generatorFactory,
            string name,
            DebugSourceSpan[] locationSpanMap,
            IList<VariableInfo>[] scopedVariables,
            IList<VariableInfo> variables,
            object customPayload) {
            FunctionInfo funcInfo = new FunctionInfo(
                generatorFactory,
                name,
                locationSpanMap,
                scopedVariables,
                variables,
                customPayload);
            
            foreach (DebugSourceSpan sourceSpan in (DebugSourceSpan[])locationSpanMap) {
                lock (sourceSpan.SourceFile.FunctionInfoMap) {
                    sourceSpan.SourceFile.FunctionInfoMap[sourceSpan] = funcInfo;
                }
            }

            return funcInfo;
        }

        internal DebugFrame CreateFrameForGenerator(FunctionInfo func) {
            DebugThread thread = GetCurrentThread();
            DebugFrame frame = new DebugFrame(thread, func);
            return frame;
        }

        internal void DispatchDebugEvent(DebugThread thread, int debugMarker, TraceEventKind eventKind, object payload) {
            DebugFrame leafFrame = null;
            bool hasFrameObject = false;

            FunctionInfo functionInfo;
            int stackDepth;
            if (eventKind != TraceEventKind.ThreadExit) {
                functionInfo = thread.GetLeafFrameFunctionInfo(out stackDepth);
            } else {
                stackDepth = Int32.MaxValue;
                functionInfo = null;
            }

            if (eventKind == TraceEventKind.Exception || eventKind == TraceEventKind.ExceptionUnwind) {
                thread.ThrownException = (Exception)payload;
            }
            thread.IsInTraceback = true;

            try {
                // Fire the event
                IDebugCallback traceHook = _traceHook;
                traceHook?.OnDebugEvent(eventKind, thread, functionInfo, debugMarker, stackDepth, payload);

                // Check if the frame object is created after the traceback.  If it's created - then we need
                // to check if we need to remap
                hasFrameObject = thread.TryGetLeafFrame(ref leafFrame);
                if (hasFrameObject) {
                    Debug.Assert(!leafFrame.InGeneratorLoop || (leafFrame.InGeneratorLoop && !leafFrame.ForceSwitchToGeneratorLoop));

                    if (leafFrame.ForceSwitchToGeneratorLoop && !leafFrame.InGeneratorLoop) {
                        throw new ForceToGeneratorLoopException();
                    }
                }
            } finally {
                if (hasFrameObject) {
                    leafFrame.IsInTraceback = false;
                }

                thread.IsInTraceback = false;
                thread.ThrownException = null;
            }
        }

        internal IDebugThreadFactory ThreadFactory {
            get { return _threadFactory; }
        }

        internal DebugThread GetCurrentThread() {
            DebugThread thread = _cachedThread;
            if (thread == null || !thread.IsCurrentThread) {
                thread = _thread.Value;
                if (thread == null) {
                    thread = _threadFactory.CreateDebugThread(this);
                    _thread.Value = thread;
                }
                Interlocked.Exchange(ref _cachedThread, thread);
            }

            return thread;
        }
    }
}
