// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using AnyPrefix.Microsoft.Scripting.Runtime;

namespace IronPython.Runtime.Exceptions {
    #region Generated BufferException

    // *** BEGIN GENERATED CODE ***
    // generated by function: gen_one_exception_specialized from: generate_exceptions.py


    [Serializable]
    public class BufferException : Exception, IPythonAwareException {
        private object _pyExceptionObject;
        private List<DynamicStackFrame> _frames;
        private TraceBack _traceback;

        public BufferException() : base() { }
        public BufferException(string msg) : base(msg) { }
        public BufferException(string message, Exception innerException)
            : base(message, innerException) {
        }
#if FEATURE_SERIALIZATION
        protected BufferException(SerializationInfo info, StreamingContext context) : base(info, context) { }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2123:OverrideLinkDemandsShouldBeIdenticalToBase")]
        public override void GetObjectData(SerializationInfo info, StreamingContext context) {
            info.AddValue("frames", _frames);
            info.AddValue("traceback", _traceback);
            base.GetObjectData(info, context);
        }
#endif

        object IPythonAwareException.PythonException {
            get { 
                if (_pyExceptionObject == null) {
                    var newEx = new PythonExceptions.BaseException(PythonExceptions.BufferError);
                    newEx.InitializeFromClr(this);
                    _pyExceptionObject = newEx;
                }
                return _pyExceptionObject; 
            }
            set { _pyExceptionObject = value; }
        }

        List<DynamicStackFrame> IPythonAwareException.Frames {
            get { return _frames; }
            set { _frames = value; }
        }

        TraceBack IPythonAwareException.TraceBack {
            get { return _traceback; }
            set { _traceback = value; }
        }
    }


    // *** END GENERATED CODE ***

    #endregion

}
