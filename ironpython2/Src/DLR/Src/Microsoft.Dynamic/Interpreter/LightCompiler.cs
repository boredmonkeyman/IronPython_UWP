﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security;

using AnyPrefix.Microsoft.Scripting.Ast;
using AnyPrefix.Microsoft.Scripting.Runtime;
using AnyPrefix.Microsoft.Scripting.Utils;
using AstUtils = AnyPrefix.Microsoft.Scripting.Ast.Utils;

namespace AnyPrefix.Microsoft.Scripting.Interpreter {
    public sealed class ExceptionHandler {
        public readonly Type ExceptionType;
        public readonly int StartIndex;
        public readonly int EndIndex;
        public readonly int LabelIndex;
        public readonly int HandlerStartIndex;

        public bool IsFault => ExceptionType == null;

        internal ExceptionHandler(int start, int end, int labelIndex, int handlerStartIndex, Type exceptionType) {
            StartIndex = start;
            EndIndex = end;
            LabelIndex = labelIndex;
            ExceptionType = exceptionType;
            HandlerStartIndex = handlerStartIndex;
        }

        public bool Matches(Type exceptionType, int index) {
            if (index < StartIndex || index >= EndIndex)
                return false;
            if (ExceptionType == null || ExceptionType.IsAssignableFrom(exceptionType)) {
                return true;
            }
            return false;
        }

        public bool IsBetterThan(ExceptionHandler other) {
            if (other == null) return true;

            if (StartIndex == other.StartIndex && EndIndex == other.EndIndex) {
                return HandlerStartIndex < other.HandlerStartIndex;
            }

            if (StartIndex > other.StartIndex) {
                Debug.Assert(EndIndex <= other.EndIndex);
                return true;
            }

            if (EndIndex < other.EndIndex) {
                Debug.Assert(StartIndex == other.StartIndex);
                return true;
            }

            return false;
        }

        internal bool IsInside(int index) {
            return index >= StartIndex && index < EndIndex;
        }

        public override string ToString() {
            return $"{(IsFault ? "fault" : "catch(" + ExceptionType.Name + ")")} [{StartIndex}-{EndIndex}] [{HandlerStartIndex}->]";
        }
    }

    [Serializable]
    public class DebugInfo {
        // TODO: readonly

        public int StartLine, EndLine;
        public int Index;
        public string FileName;
        public bool IsClear;
        private static readonly DebugInfoComparer _debugComparer = new DebugInfoComparer();

        private class DebugInfoComparer : IComparer<DebugInfo> {
            //We allow comparison between int and DebugInfo here
            int IComparer<DebugInfo>.Compare(DebugInfo d1, DebugInfo d2) {
                if (d1.Index > d2.Index) return 1;
                if (d1.Index == d2.Index) return 0;
                return -1;
            }
        }
        
        public static DebugInfo GetMatchingDebugInfo(DebugInfo[] debugInfos, int index) {
            //Create a faked DebugInfo to do the search
            DebugInfo d = new DebugInfo { Index = index };

            //to find the closest debug info before the current index

            int i = Array.BinarySearch<DebugInfo>(debugInfos, d, _debugComparer);
            if (i < 0) {
                //~i is the index for the first bigger element
                //if there is no bigger element, ~i is the length of the array
                i = ~i;
                if (i == 0) {
                    return null;
                }
                //return the last one that is smaller
                i = i - 1;
            }

            return debugInfos[i];
        }

        public override string ToString()
        {
            return IsClear ? $"{Index}: clear" : $"{Index}: [{StartLine}-{EndLine}] '{FileName}'";
        }
    }

    // TODO:
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    [Serializable]
    public struct InterpretedFrameInfo {
        public readonly string MethodName;
        
        // TODO:
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2104:DoNotDeclareReadOnlyMutableReferenceTypes")]
        public readonly DebugInfo DebugInfo;

        public InterpretedFrameInfo(string methodName, DebugInfo info) {
            MethodName = methodName;
            DebugInfo = info;
        }

        public override string ToString() {
            return MethodName + (DebugInfo != null ? ": " + DebugInfo : null);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1506:AvoidExcessiveClassCoupling")]
    public sealed class LightCompiler {
        internal const int DefaultCompilationThreshold = 32;

        // zero: sync compilation
        private readonly int _compilationThreshold;

        private readonly LocalVariables _locals = new LocalVariables();

        private readonly List<ExceptionHandler> _handlers = new List<ExceptionHandler>();
        
        private readonly List<DebugInfo> _debugInfos = new List<DebugInfo>();
        private readonly HybridReferenceDictionary<LabelTarget, LabelInfo> _treeLabels = new HybridReferenceDictionary<LabelTarget, LabelInfo>();
        private LabelScopeInfo _labelBlock = new LabelScopeInfo(null, LabelScopeKind.Lambda);

        private readonly Stack<ParameterExpression> _exceptionForRethrowStack = new Stack<ParameterExpression>();

        // Set to true to force compiliation of this lambda.
        // This disables the interpreter for this lambda. We still need to
        // walk it, however, to resolve variables closed over from the parent
        // lambdas (because they may be interpreted).
        private bool _forceCompile;

        private readonly LightCompiler _parent;

        private static readonly LocalDefinition[] EmptyLocals = new LocalDefinition[0];

        internal LightCompiler(int compilationThreshold) {
            Instructions = new InstructionList();
            _compilationThreshold = compilationThreshold < 0 ? DefaultCompilationThreshold : compilationThreshold;
        }

        private LightCompiler(LightCompiler parent)
            : this(parent._compilationThreshold) {
            _parent = parent;
        }

        public InstructionList Instructions { get; }

        public LocalVariables Locals => _locals;

        internal static Expression Unbox(Expression strongBoxExpression) {
            return Expression.Field(strongBoxExpression, typeof(StrongBox<object>).GetDeclaredField("Value"));
        }

        internal LightDelegateCreator CompileTop(LambdaExpression node) {
            foreach (var p in node.Parameters) {
                var local = _locals.DefineLocal(p, 0);
                Instructions.EmitInitializeParameter(local.Index);
            }

            Compile(node.Body);
            
            // pop the result of the last expression:
            if (node.Body.Type != typeof(void) && node.ReturnType == typeof(void)) {
                Instructions.EmitPop();
            }

            Debug.Assert(Instructions.CurrentStackDepth == (node.ReturnType != typeof(void) ? 1 : 0));

            return new LightDelegateCreator(MakeInterpreter(node.Name), node);
        }

        internal LightDelegateCreator CompileTop(LightLambdaExpression node) {
            foreach (var p in node.Parameters) {
                var local = _locals.DefineLocal(p, 0);
                Instructions.EmitInitializeParameter(local.Index);
            }

            Compile(node.Body);

            // pop the result of the last expression:
            if (node.Body.Type != typeof(void) && node.ReturnType == typeof(void)) {
                Instructions.EmitPop();
            }

            Debug.Assert(Instructions.CurrentStackDepth == (node.ReturnType != typeof(void) ? 1 : 0));

            return new LightDelegateCreator(MakeInterpreter(node.Name), node);
        }

        private Interpreter MakeInterpreter(string lambdaName) {
            if (_forceCompile) {
                return null;
            }

            var handlers = _handlers.ToArray();
            var debugInfos = _debugInfos.ToArray();

            return new Interpreter(lambdaName, _locals, GetBranchMapping(), Instructions.ToArray(), handlers, debugInfos, _compilationThreshold);
        }

        private void CompileConstantExpression(Expression expr) {
            var node = (ConstantExpression)expr;
            Instructions.EmitLoad(node.Value, node.Type);
        }

        private void CompileDefaultExpression(Expression expr) {
            CompileDefaultExpression(expr.Type);
        }

        private void CompileDefaultExpression(Type type) {
            if (type == typeof(void))
                return;
            if (type.IsValueType) {
                object value = ScriptingRuntimeHelpers.GetPrimitiveDefaultValue(type);
                if (value != null) {
                    Instructions.EmitLoad(value);
                } else {
                    Instructions.EmitDefaultValue(type);
                }
            } else {
                Instructions.EmitLoad(null);
            }
        }

        private LocalVariable EnsureAvailableForClosure(ParameterExpression expr) {
            if (_locals.TryGetLocalOrClosure(expr, out LocalVariable local)) {
                if (!local.InClosure && !local.IsBoxed) {
                    _locals.Box(expr, Instructions);
                }
                return local;
            }

            if (_parent != null) {
                _parent.EnsureAvailableForClosure(expr);
                return _locals.AddClosureVariable(expr);
            }

            throw new InvalidOperationException("unbound variable: " + expr);
        }

        private void EnsureVariable(ParameterExpression variable) {
            if (!_locals.ContainsVariable(variable)) {
                EnsureAvailableForClosure(variable);
            }
        }

        private LocalVariable ResolveLocal(ParameterExpression variable) {
            if (!_locals.TryGetLocalOrClosure(variable, out LocalVariable local)) {
                local = EnsureAvailableForClosure(variable);
            }
            return local;
        }

        public void CompileGetVariable(ParameterExpression variable) {
            LocalVariable local = ResolveLocal(variable);

            if (local.InClosure) {
                Instructions.EmitLoadLocalFromClosure(local.Index);
            } else if (local.IsBoxed) {
                Instructions.EmitLoadLocalBoxed(local.Index);
            } else {
                Instructions.EmitLoadLocal(local.Index);
            }

            Instructions.SetDebugCookie(variable.Name);
        }

        public void CompileGetBoxedVariable(ParameterExpression variable) {
            LocalVariable local = ResolveLocal(variable);

            if (local.InClosure) {
                Instructions.EmitLoadLocalFromClosureBoxed(local.Index);
            } else {
                Debug.Assert(local.IsBoxed);
                Instructions.EmitLoadLocal(local.Index);
            }

            Instructions.SetDebugCookie(variable.Name);
        }

        public void CompileSetVariable(ParameterExpression variable, bool isVoid) {
            LocalVariable local = ResolveLocal(variable);

            if (local.InClosure) {
                if (isVoid) {
                    Instructions.EmitStoreLocalToClosure(local.Index);
                } else {
                    Instructions.EmitAssignLocalToClosure(local.Index);
                }
            } else if (local.IsBoxed) {
                if (isVoid) {
                    Instructions.EmitStoreLocalBoxed(local.Index);
                } else {
                    Instructions.EmitAssignLocalBoxed(local.Index);
                }
            } else {
                if (isVoid) {
                    Instructions.EmitStoreLocal(local.Index);
                } else {
                    Instructions.EmitAssignLocal(local.Index);
                }
            }

            Instructions.SetDebugCookie(variable.Name);
        }

        public void CompileParameterExpression(Expression expr) {
            var node = (ParameterExpression)expr;
            CompileGetVariable(node);
        }

        private void CompileBlockExpression(Expression expr, bool asVoid) {
            var node = (BlockExpression)expr;
            var end = CompileBlockStart(node);

            var lastExpression = node.Expressions[node.Expressions.Count - 1];
            Compile(lastExpression, asVoid);
            CompileBlockEnd(end);
        }

        private LocalDefinition[] CompileBlockStart(BlockExpression node) {
            var start = Instructions.Count;
            
            LocalDefinition[] locals;
            var variables = node.Variables;
            if (variables.Count != 0) {
                // TODO: basic flow analysis so we don't have to initialize all
                // variables.
                locals = new LocalDefinition[variables.Count];
                int localCnt = 0;
                foreach (var variable in variables) {
                    var local = _locals.DefineLocal(variable, start);
                    locals[localCnt++] = local;

                    Instructions.EmitInitializeLocal(local.Index, variable.Type);
                    Instructions.SetDebugCookie(variable.Name);
                }
            } else {
                locals = EmptyLocals;
            }

            for (int i = 0; i < node.Expressions.Count - 1; i++) {
                CompileAsVoid(node.Expressions[i]);
            }
            return locals;
        }

        private void CompileBlockEnd(LocalDefinition[] locals) {
            foreach (var local in locals) {
                _locals.UndefineLocal(local, Instructions.Count);
            }
        }

        private void CompileIndexExpression(Expression expr) {
            var index = (IndexExpression)expr;

            // instance:
            if (index.Object != null) {
                Compile(index.Object);
            }

            // indexes, byref args not allowed.
            foreach (var arg in index.Arguments) {
                Compile(arg);
            }

            if (index.Indexer != null) {
                EmitCall(index.Indexer.GetGetMethod(true));
            } else if (index.Arguments.Count != 1) {
                EmitCall(index.Object.Type.GetMethod("Get", BindingFlags.Public | BindingFlags.Instance));
            } else {
                Instructions.EmitGetArrayItem(index.Object.Type);
            }
        }

        private void CompileIndexAssignment(BinaryExpression node, bool asVoid) {
            var index = (IndexExpression)node.Left;

            if (!asVoid) {
                throw new NotImplementedException();
            }

            // instance:
            if (index.Object != null) {
                Compile(index.Object);
            }

            // indexes, byref args not allowed.
            foreach (var arg in index.Arguments) {
                Compile(arg);
            }

            // value:
            Compile(node.Right);

            if (index.Indexer != null) {
                EmitCall(index.Indexer.GetSetMethod(true));
            } else if (index.Arguments.Count != 1) {
                EmitCall(index.Object.Type.GetMethod("Set", BindingFlags.Public | BindingFlags.Instance));
            } else {
                Instructions.EmitSetArrayItem(index.Object.Type);
            }
        }

        private void CompileMemberAssignment(BinaryExpression node, bool asVoid) {
            var member = (MemberExpression)node.Left;

            PropertyInfo pi = member.Member as PropertyInfo;
            if (pi != null) {
                var method = pi.GetSetMethod(true);
                Compile(member.Expression);
                Compile(node.Right);

                int start = Instructions.Count;
                if (!asVoid) {
                    LocalDefinition local = _locals.DefineLocal(Expression.Parameter(node.Right.Type), start);
                    Instructions.EmitAssignLocal(local.Index);
                    EmitCall(method);
                    Instructions.EmitLoadLocal(local.Index);
                    _locals.UndefineLocal(local, Instructions.Count);
                } else {
                    EmitCall(method);
                }
                return;
            }

            FieldInfo fi = member.Member as FieldInfo;
            if (fi != null) {
                if (member.Expression != null) {
                    Compile(member.Expression);
                }
                Compile(node.Right);

                int start = Instructions.Count;
                if (!asVoid) {
                    LocalDefinition local = _locals.DefineLocal(Expression.Parameter(node.Right.Type), start);
                    Instructions.EmitAssignLocal(local.Index);
                    Instructions.EmitStoreField(fi);
                    Instructions.EmitLoadLocal(local.Index);
                    _locals.UndefineLocal(local, Instructions.Count);
                } else {
                    Instructions.EmitStoreField(fi);
                }
                return;
            }

            throw new NotImplementedException();
        }

        private void CompileVariableAssignment(BinaryExpression node, bool asVoid) {
            Compile(node.Right);

            var target = (ParameterExpression)node.Left;
            CompileSetVariable(target, asVoid);
        }

        private void CompileAssignBinaryExpression(Expression expr, bool asVoid) {
            var node = (BinaryExpression)expr;

            switch (node.Left.NodeType) {
                case ExpressionType.Index:
                    CompileIndexAssignment(node, asVoid); 
                    break;
                case ExpressionType.MemberAccess:
                    CompileMemberAssignment(node, asVoid); 
                    break;
                case ExpressionType.Parameter:
                case ExpressionType.Extension:
                    CompileVariableAssignment(node, asVoid); 
                    break;
                default:
                    throw new InvalidOperationException("Invalid lvalue for assignment: " + node.Left.NodeType);
            }
        }

        private void CompileBinaryExpression(Expression expr) {
            var node = (BinaryExpression)expr;

            if (node.Method != null) {
                Compile(node.Left);
                Compile(node.Right);
                EmitCall(node.Method);
            } else {
                switch (node.NodeType) {
                    case ExpressionType.ArrayIndex:
                        Debug.Assert(node.Right.Type == typeof(int));
                        Compile(node.Left);
                        Compile(node.Right);
                        Instructions.EmitGetArrayItem(node.Left.Type);
                        return;

                    case ExpressionType.Add:
                    case ExpressionType.AddChecked:
                    case ExpressionType.Subtract:
                    case ExpressionType.SubtractChecked:
                    case ExpressionType.Multiply:
                    case ExpressionType.MultiplyChecked:
                    case ExpressionType.Divide:
                        CompileArithmetic(node.NodeType, node.Left, node.Right);
                        return;

                    case ExpressionType.Equal:
                        CompileEqual(node.Left, node.Right);
                        return;

                    case ExpressionType.NotEqual:
                        CompileNotEqual(node.Left, node.Right);
                        return;

                    case ExpressionType.LessThan:
                    case ExpressionType.LessThanOrEqual:
                    case ExpressionType.GreaterThan:
                    case ExpressionType.GreaterThanOrEqual:
                        CompileComparison(node.NodeType, node.Left, node.Right);
                        return;

                    default:
                        throw new NotImplementedException(node.NodeType.ToString());
                }
            }
        }

        private void CompileEqual(Expression left, Expression right) {
            Debug.Assert(left.Type == right.Type || !left.Type.IsValueType && !right.Type.IsValueType);
            Compile(left);
            Compile(right);
            Instructions.EmitEqual(left.Type);
        }

        private void CompileNotEqual(Expression left, Expression right) {
            Debug.Assert(left.Type == right.Type || !left.Type.IsValueType && !right.Type.IsValueType);
            Compile(left);
            Compile(right);
            Instructions.EmitNotEqual(left.Type);
        }

        private void CompileComparison(ExpressionType nodeType, Expression left, Expression right) {
            Debug.Assert(left.Type == right.Type && left.Type.IsNumeric());

            // TODO:
            // if (TypeUtils.IsNullableType(left.Type) && liftToNull) ...

            Compile(left);
            Compile(right);
            
            switch (nodeType) {
                case ExpressionType.LessThan: Instructions.EmitLessThan(left.Type); break;
                case ExpressionType.LessThanOrEqual: Instructions.EmitLessThanOrEqual(left.Type); break;
                case ExpressionType.GreaterThan: Instructions.EmitGreaterThan(left.Type); break;
                case ExpressionType.GreaterThanOrEqual: Instructions.EmitGreaterThanOrEqual(left.Type); break;
                default: throw Assert.Unreachable;
            }
        }

        private void CompileArithmetic(ExpressionType nodeType, Expression left, Expression right) {
            Debug.Assert(left.Type == right.Type && left.Type.IsArithmetic());
            Compile(left);
            Compile(right);
            switch (nodeType) {
                case ExpressionType.Add: Instructions.EmitAdd(left.Type, false); break;
                case ExpressionType.AddChecked: Instructions.EmitAdd(left.Type, true); break;
                case ExpressionType.Subtract: Instructions.EmitSub(left.Type, false); break;
                case ExpressionType.SubtractChecked: Instructions.EmitSub(left.Type, true); break;
                case ExpressionType.Multiply: Instructions.EmitMul(left.Type, false); break;
                case ExpressionType.MultiplyChecked: Instructions.EmitMul(left.Type, true); break;
                case ExpressionType.Divide: Instructions.EmitDiv(left.Type); break;
                default: throw Assert.Unreachable;
            }
        }

        private void CompileConvertUnaryExpression(Expression expr) {
            var node = (UnaryExpression)expr;
            if (node.Method != null) {
                Compile(node.Operand);

                // We should be able to ignore Int32ToObject
                if (node.Method != Runtime.ScriptingRuntimeHelpers.Int32ToObjectMethod) {
                    EmitCall(node.Method);
                }
            } else if (node.Type == typeof(void)) {
                CompileAsVoid(node.Operand);
            } else {
                Compile(node.Operand);
                CompileConvertToType(node.Operand.Type, node.Type, node.NodeType == ExpressionType.ConvertChecked);
            }
        }

        private void CompileConvertToType(Type typeFrom, Type typeTo, bool isChecked) {
            Debug.Assert(typeFrom != typeof(void) && typeTo != typeof(void));

            if (TypeUtils.AreEquivalent(typeTo, typeFrom)) {
                return;
            }

            TypeCode from = typeFrom.GetTypeCode();
            TypeCode to = typeTo.GetTypeCode();
            if (TypeUtils.IsNumeric(from) && TypeUtils.IsNumeric(to)) {
                if (isChecked) {
                    Instructions.EmitNumericConvertChecked(from, to);
                } else {
                    Instructions.EmitNumericConvertUnchecked(from, to);
                }
                return;
            }

            // TODO: Conversions to a super-class or implemented interfaces are no-op. 
            // A conversion to a non-implemented interface or an unrelated class, etc. should fail.
            return;
        }

        private void CompileNotExpression(UnaryExpression node) {
            if (node.Operand.Type == typeof(bool)) {
                Compile(node.Operand);
                Instructions.EmitNot();
            } else {
                throw new NotImplementedException();
            }
        }

        private void CompileUnaryExpression(Expression expr) {
            var node = (UnaryExpression)expr;
            
            if (node.Method != null) {
                Compile(node.Operand);
                EmitCall(node.Method);
            } else {
                switch (node.NodeType) {
                    case ExpressionType.Not:
                        CompileNotExpression(node);
                        return;
                    case ExpressionType.TypeAs:
                        CompileTypeAsExpression(node);
                        return;
                    default:
                        throw new NotImplementedException(node.NodeType.ToString());
                }
            }
        }

        private void CompileAndAlsoBinaryExpression(Expression expr) {
            CompileLogicalBinaryExpression(expr, true);
        }

        private void CompileOrElseBinaryExpression(Expression expr) {
            CompileLogicalBinaryExpression(expr, false);
        }

        private void CompileLogicalBinaryExpression(Expression expr, bool andAlso) {
            var node = (BinaryExpression)expr;
            if (node.Method != null) {
                throw new NotImplementedException();
            }

            Debug.Assert(node.Left.Type == node.Right.Type);

            if (node.Left.Type == typeof(bool)) {
                var elseLabel = Instructions.MakeLabel();
                var endLabel = Instructions.MakeLabel();
                Compile(node.Left);
                if (andAlso) {
                    Instructions.EmitBranchFalse(elseLabel);
                } else {
                    Instructions.EmitBranchTrue(elseLabel);
                }
                Compile(node.Right);
                Instructions.EmitBranch(endLabel, false, true);
                Instructions.MarkLabel(elseLabel);
                Instructions.EmitLoad(!andAlso);
                Instructions.MarkLabel(endLabel);
                return;
            }

            Debug.Assert(node.Left.Type == typeof(bool?));
            throw new NotImplementedException();
        }

        private void CompileConditionalExpression(Expression expr, bool asVoid) {
            var node = (ConditionalExpression)expr;
            Compile(node.Test);

            if (node.IfTrue == AstUtils.Empty()) {
                var endOfFalse = Instructions.MakeLabel();
                Instructions.EmitBranchTrue(endOfFalse);
                Compile(node.IfFalse, asVoid);
                Instructions.MarkLabel(endOfFalse);
            } else {
                var endOfTrue = Instructions.MakeLabel();
                Instructions.EmitBranchFalse(endOfTrue);
                Compile(node.IfTrue, asVoid);

                if (node.IfFalse != AstUtils.Empty()) {
                    var endOfFalse = Instructions.MakeLabel();
                    Instructions.EmitBranch(endOfFalse, false, !asVoid);
                    Instructions.MarkLabel(endOfTrue);
                    Compile(node.IfFalse, asVoid);
                    Instructions.MarkLabel(endOfFalse);
                } else {
                    Instructions.MarkLabel(endOfTrue);
                }
            }
        }

        #region Loops

        private void CompileLoopExpression(Expression expr) {
            var node = (LoopExpression)expr;
            var enterLoop = new EnterLoopInstruction(node, _locals, _compilationThreshold, Instructions.Count);

            PushLabelBlock(LabelScopeKind.Statement);
            LabelInfo breakLabel = DefineLabel(node.BreakLabel);
            LabelInfo continueLabel = DefineLabel(node.ContinueLabel);

            Instructions.MarkLabel(continueLabel.GetLabel(this));

            // emit loop body:
            Instructions.Emit(enterLoop);
            CompileAsVoid(node.Body);

            // emit loop branch:
            Instructions.EmitBranch(continueLabel.GetLabel(this), expr.Type != typeof(void), false);

            Instructions.MarkLabel(breakLabel.GetLabel(this));

            PopLabelBlock(LabelScopeKind.Statement);

            enterLoop.FinishLoop(Instructions.Count);
        }

        #endregion

        private void CompileSwitchExpression(Expression expr) {
            var node = (SwitchExpression)expr;

            // Currently only supports int test values, with no method
            if (node.SwitchValue.Type != typeof(int) || node.Comparison != null) {
                throw new NotImplementedException();
            }

            // Test values must be constant
            if (!node.Cases.All(c => c.TestValues.All(t => t is ConstantExpression))) {
                throw new NotImplementedException();
            }
            LabelInfo end = DefineLabel(null);
            bool hasValue = node.Type != typeof(void);

            Compile(node.SwitchValue);
            var caseDict = new MyDictionary<int, int>();
            int switchIndex = Instructions.Count;
            Instructions.EmitSwitch(caseDict);

            if (node.DefaultBody != null) {
                Compile(node.DefaultBody);
            } else {
                Debug.Assert(!hasValue);
            }
            Instructions.EmitBranch(end.GetLabel(this), false, hasValue);

            for (int i = 0; i < node.Cases.Count; i++) {
                var switchCase = node.Cases[i];

                int caseOffset = Instructions.Count - switchIndex;
                foreach (ConstantExpression testValue in switchCase.TestValues) {
                    caseDict[(int)testValue.Value] = caseOffset;
                }

                Compile(switchCase.Body);

                if (i < node.Cases.Count - 1) {
                    Instructions.EmitBranch(end.GetLabel(this), false, hasValue);
                }
            }

            Instructions.MarkLabel(end.GetLabel(this));
        }

        private void CompileLabelExpression(Expression expr) {
            var node = (LabelExpression)expr;

            // If we're an immediate child of a block, our label will already
            // be defined. If not, we need to define our own block so this
            // label isn't exposed except to its own child expression.
            LabelInfo label = null;

            if (_labelBlock.Kind == LabelScopeKind.Block) {
                _labelBlock.TryGetLabelInfo(node.Target, out label);

                // We're in a block but didn't find our label, try switch
                if (label == null && _labelBlock.Parent.Kind == LabelScopeKind.Switch) {
                    _labelBlock.Parent.TryGetLabelInfo(node.Target, out label);
                }

                // if we're in a switch or block, we should've found the label
                Debug.Assert(label != null);
            }

            if (label == null) {
                label = DefineLabel(node.Target);
            }

            if (node.DefaultValue != null) {
                if (node.Target.Type == typeof(void)) {
                    CompileAsVoid(node.DefaultValue);
                } else {
                    Compile(node.DefaultValue);
                }
            }

            Instructions.MarkLabel(label.GetLabel(this));
        }

        private void CompileGotoExpression(Expression expr) {
            var node = (GotoExpression)expr;
            var labelInfo = ReferenceLabel(node.Target);

            if (node.Value != null) {
                Compile(node.Value);
            }

            Instructions.EmitGoto(labelInfo.GetLabel(this), node.Type != typeof(void), node.Value != null && node.Value.Type != typeof(void));
        }

        public BranchLabel GetBranchLabel(LabelTarget target) {
            return ReferenceLabel(target).GetLabel(this);
        }

        public void PushLabelBlock(LabelScopeKind type) {
            _labelBlock = new LabelScopeInfo(_labelBlock, type);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "kind")]
        public void PopLabelBlock(LabelScopeKind kind) {
            Debug.Assert(_labelBlock != null && _labelBlock.Kind == kind);
            _labelBlock = _labelBlock.Parent;
        }

        private LabelInfo EnsureLabel(LabelTarget node) {
            if (!_treeLabels.TryGetValue(node, out LabelInfo result)) {
                _treeLabels[node] = result = new LabelInfo(node);
            }
            return result;
        }

        private LabelInfo ReferenceLabel(LabelTarget node) {
            LabelInfo result = EnsureLabel(node);
            result.Reference(_labelBlock);
            return result;
        }

        internal LabelInfo DefineLabel(LabelTarget node) {
            if (node == null) {
                return new LabelInfo(null);
            }
            LabelInfo result = EnsureLabel(node);
            result.Define(_labelBlock);
            return result;
        }

        private bool TryPushLabelBlock(Expression node) {
            // Anything that is "statement-like" -- e.g. has no associated
            // stack state can be jumped into, with the exception of try-blocks
            // We indicate this by a "Block"
            // 
            // Otherwise, we push an "Expression" to indicate that it can't be
            // jumped into
            switch (node.NodeType) {
                default:
                    if (_labelBlock.Kind != LabelScopeKind.Expression) {
                        PushLabelBlock(LabelScopeKind.Expression);
                        return true;
                    }
                    return false;
                case ExpressionType.Label:
                    // LabelExpression is a bit special, if it's directly in a
                    // block it becomes associate with the block's scope. Same
                    // thing if it's in a switch case body.
                    if (_labelBlock.Kind == LabelScopeKind.Block) {
                        var label = ((LabelExpression)node).Target;
                        if (_labelBlock.ContainsTarget(label)) {
                            return false;
                        }
                        if (_labelBlock.Parent.Kind == LabelScopeKind.Switch &&
                            _labelBlock.Parent.ContainsTarget(label)) {
                            return false;
                        }
                    }
                    PushLabelBlock(LabelScopeKind.Statement);
                    return true;
                case ExpressionType.Block:
                    PushLabelBlock(LabelScopeKind.Block);
                    // Labels defined immediately in the block are valid for
                    // the whole block.
                    if (_labelBlock.Parent.Kind != LabelScopeKind.Switch) {
                        DefineBlockLabels(node);
                    }
                    return true;
                case ExpressionType.Switch:
                    PushLabelBlock(LabelScopeKind.Switch);
                    // Define labels inside of the switch cases so theyare in
                    // scope for the whole switch. This allows "goto case" and
                    // "goto default" to be considered as local jumps.
                    var @switch = (SwitchExpression)node;
                    foreach (SwitchCase c in @switch.Cases) {
                        DefineBlockLabels(c.Body);
                    }
                    DefineBlockLabels(@switch.DefaultBody);
                    return true;

                // Remove this when Convert(Void) goes away.
                case ExpressionType.Convert:
                    if (node.Type != typeof(void)) {
                        // treat it as an expression
                        goto default;
                    }
                    PushLabelBlock(LabelScopeKind.Statement);
                    return true;

                case ExpressionType.Conditional:
                case ExpressionType.Loop:
                case ExpressionType.Goto:
                    PushLabelBlock(LabelScopeKind.Statement);
                    return true;
            }
        }

        private void DefineBlockLabels(Expression node) {
            if (!(node is BlockExpression block)) {
                return;
            }

            for (int i = 0, n = block.Expressions.Count; i < n; i++) {
                Expression e = block.Expressions[i];

                if (e is LabelExpression label) {
                    DefineLabel(label.Target);
                }
            }
        }

        private HybridReferenceDictionary<LabelTarget, BranchLabel> GetBranchMapping() {
            var newLabelMapping = new HybridReferenceDictionary<LabelTarget, BranchLabel>(_treeLabels.Count);
            foreach (var kvp in _treeLabels) {
                newLabelMapping[kvp.Key] = kvp.Value.GetLabel(this);
            }
            return newLabelMapping;
        }

        private void CompileThrowUnaryExpression(Expression expr, bool asVoid) {
            var node = (UnaryExpression)expr;

            if (node.Operand == null) {
                CompileParameterExpression(_exceptionForRethrowStack.Peek());
                if (asVoid) {
                    Instructions.EmitRethrowVoid();
                } else {
                    Instructions.EmitRethrow();
                }
            } else {
                Compile(node.Operand);
                if (asVoid) {
                    Instructions.EmitThrowVoid();
                } else {
                    Instructions.EmitThrow();
                }
            }

        }

        // TODO: remove (replace by true fault support)
        private bool EndsWithRethrow(Expression expr) {
            if (expr.NodeType == ExpressionType.Throw) {
                var node = (UnaryExpression)expr;
                return node.Operand == null;
            }

            if (expr is BlockExpression block) {
                return EndsWithRethrow(block.Expressions[block.Expressions.Count - 1]);
            }
            return false;
        }


        // TODO: remove (replace by true fault support)
        private void CompileAsVoidRemoveRethrow(Expression expr) {
            int stackDepth = Instructions.CurrentStackDepth;

            if (expr.NodeType == ExpressionType.Throw) {
                Debug.Assert(((UnaryExpression)expr).Operand == null);
                return;
            }

            var node = (BlockExpression)expr;
            var end = CompileBlockStart(node);

            CompileAsVoidRemoveRethrow(node.Expressions[node.Expressions.Count - 1]);

            Debug.Assert(stackDepth == Instructions.CurrentStackDepth);

            CompileBlockEnd(end);
        }

        private void CompileTryExpression(Expression expr) {
            var node = (TryExpression)expr;

            BranchLabel end = Instructions.MakeLabel();
            BranchLabel gotoEnd = Instructions.MakeLabel();

            int tryStart = Instructions.Count;

            BranchLabel startOfFinally = null;
            if (node.Finally != null) {
                startOfFinally = Instructions.MakeLabel();
                Instructions.EmitEnterTryFinally(startOfFinally);
            }

            PushLabelBlock(LabelScopeKind.Try);
            Compile(node.Body);

            bool hasValue = node.Body.Type != typeof(void);
            int tryEnd = Instructions.Count;

            // handlers jump here:
            Instructions.MarkLabel(gotoEnd);
            Instructions.EmitGoto(end, hasValue, hasValue);
            
            // keep the result on the stack:     
            if (node.Handlers.Count > 0) {
                // TODO: emulates faults (replace by true fault support)
                if (node.Finally == null && node.Handlers.Count == 1) {
                    var handler = node.Handlers[0];
                    if (handler.Filter == null && handler.Test == typeof(Exception) && handler.Variable == null) {
                        if (EndsWithRethrow(handler.Body)) {
                            if (hasValue) {
                                Instructions.EmitEnterExceptionHandlerNonVoid();
                            } else {
                                Instructions.EmitEnterExceptionHandlerVoid();
                            }

                            // at this point the stack balance is prepared for the hidden exception variable:
                            int handlerLabel = Instructions.MarkRuntimeLabel();
                            int handlerStart = Instructions.Count;

                            CompileAsVoidRemoveRethrow(handler.Body);
                            Instructions.EmitLeaveFault(hasValue);
                            Instructions.MarkLabel(end);

                            _handlers.Add(new ExceptionHandler(tryStart, tryEnd, handlerLabel, handlerStart, null));
                            PopLabelBlock(LabelScopeKind.Try);
                            return;
                        }
                    }
                }

                foreach (var handler in node.Handlers) {
                    PushLabelBlock(LabelScopeKind.Catch);

                    if (handler.Filter != null) {
                        //PushLabelBlock(LabelScopeKind.Filter);
                        throw new NotImplementedException();
                        //PopLabelBlock(LabelScopeKind.Filter);
                    }

                    var parameter = handler.Variable ?? Expression.Parameter(handler.Test);

                    var local = _locals.DefineLocal(parameter, Instructions.Count);
                    _exceptionForRethrowStack.Push(parameter);

                    // add a stack balancing nop instruction (exception handling pushes the current exception):
                    if (hasValue) {
                        Instructions.EmitEnterExceptionHandlerNonVoid();
                    } else {
                        Instructions.EmitEnterExceptionHandlerVoid();
                    }

                    // at this point the stack balance is prepared for the hidden exception variable:
                    int handlerLabel = Instructions.MarkRuntimeLabel();
                    int handlerStart = Instructions.Count;

                    CompileSetVariable(parameter, true);
                    Compile(handler.Body);

                    _exceptionForRethrowStack.Pop();

                    // keep the value of the body on the stack:
                    Debug.Assert(hasValue == (handler.Body.Type != typeof(void)));
                    Instructions.EmitLeaveExceptionHandler(hasValue, gotoEnd);

                    _handlers.Add(new ExceptionHandler(tryStart, tryEnd, handlerLabel, handlerStart, handler.Test));

                    PopLabelBlock(LabelScopeKind.Catch);
                
                    _locals.UndefineLocal(local, Instructions.Count);
                }

                if (node.Fault != null) {
                    throw new NotImplementedException();
                }
            }
            
            if (node.Finally != null) {
                PushLabelBlock(LabelScopeKind.Finally);

                Instructions.MarkLabel(startOfFinally);
                Instructions.EmitEnterFinally();
                CompileAsVoid(node.Finally);
                Instructions.EmitLeaveFinally();

                PopLabelBlock(LabelScopeKind.Finally);
            }

            Instructions.MarkLabel(end);

            PopLabelBlock(LabelScopeKind.Try);
        }

        private void CompileDynamicExpression(Expression expr) {
            var node = (DynamicExpression)expr;

            foreach (var arg in node.Arguments) {
                Compile(arg);
            }

            Instructions.EmitDynamic(node.DelegateType, node.Binder);
        }

        private void CompileMethodCallExpression(Expression expr) {
            var node = (MethodCallExpression)expr;

            var parameters = node.Method.GetParameters();

            // TODO:
            // Support pass by reference.
            // Note that LoopCompiler needs to be updated too.

            // force compilation for now for ref types
            // also could be a mutable value type, Delegate.CreateDelegate and MethodInfo.Invoke both can't handle this, we
            // need to generate code.
            if (!CollectionUtils.TrueForAll(parameters, (p) => !p.ParameterType.IsByRef) ||
                (!node.Method.IsStatic && node.Method.DeclaringType.IsValueType && !node.Method.DeclaringType.IsPrimitive)) {
                _forceCompile = true;
            }


            if (!node.Method.IsStatic) {
                Compile(node.Object);
            }

            foreach (var arg in node.Arguments) {
                Compile(arg);
            }

            EmitCall(node.Method, parameters);
        }

        public void EmitCall(MethodInfo method) {
            EmitCall(method, method.GetParameters());
        }

        public void EmitCall(MethodInfo method, ParameterInfo[] parameters) {
            Instruction instruction;

            try {
                instruction = CallInstruction.Create(method, parameters);
            } catch (SecurityException) {
                _forceCompile = true;
                
                Instructions.Emit(new PopNInstruction((method.IsStatic ? 0 : 1) + parameters.Length));
                if (method.ReturnType != typeof(void)) {
                    Instructions.EmitLoad(null);
                }

                return;
            }

            Instructions.Emit(instruction);
        }

        private void CompileNewExpression(Expression expr) {
            var node = (NewExpression)expr;

            if (node.Constructor != null) {
                var parameters = node.Constructor.GetParameters();
                if (!CollectionUtils.TrueForAll(parameters, (p) => !p.ParameterType.IsByRef)
#if FEATURE_LCG
                     || node.Constructor.DeclaringType == typeof(DynamicMethod)
#endif
                ) {
                    _forceCompile = true;
                }
            }

            if (node.Constructor != null) {
                foreach (var arg in node.Arguments) {
                    Compile(arg);
                }
                Instructions.EmitNew(node.Constructor);
            } else {
                Debug.Assert(expr.Type.IsValueType);
                Instructions.EmitDefaultValue(node.Type);
            }
        }

        private void CompileMemberExpression(Expression expr) {
            var node = (MemberExpression)expr;

            var member = node.Member;
            FieldInfo fi = member as FieldInfo;
            if (fi != null) {
                if (fi.IsLiteral) {
                    Instructions.EmitLoad(fi.GetRawConstantValue(), fi.FieldType);
                } else if (fi.IsStatic) {
                    if (fi.IsInitOnly) {
                        Instructions.EmitLoad(fi.GetValue(null), fi.FieldType);
                    } else {
                        Instructions.EmitLoadField(fi);
                    }
                } else {
                    Compile(node.Expression);
                    Instructions.EmitLoadField(fi);
                }
                return;
            }

            PropertyInfo pi = member as PropertyInfo;
            if (pi != null) {
                var method = pi.GetGetMethod(true);
                if (node.Expression != null) {
                    Compile(node.Expression);
                }
                EmitCall(method);
                return;
            }

            throw new System.NotImplementedException();
        }

        private void CompileNewArrayExpression(Expression expr) {
            var node = (NewArrayExpression)expr;

            foreach (var arg in node.Expressions) {
                Compile(arg);
            }

            Type elementType = node.Type.GetElementType();
            int rank = node.Expressions.Count;

            if (node.NodeType == ExpressionType.NewArrayInit) {
                Instructions.EmitNewArrayInit(elementType, rank);
            } else if (node.NodeType == ExpressionType.NewArrayBounds) {
                if (rank == 1) {
                    Instructions.EmitNewArray(elementType);
                } else {
                    Instructions.EmitNewArrayBounds(elementType, rank);
                }
            } else {
                throw new System.NotImplementedException();
            }
        }

        private void CompileExtensionExpression(Expression expr) {
            if (expr is IInstructionProvider instructionProvider) {
                instructionProvider.AddInstructions(this);
                return;
            }

            if (expr.CanReduce) {
                Compile(expr.Reduce());
            } else {
                throw new System.NotImplementedException();
            }
        }

        private void CompileDebugInfoExpression(Expression expr) {
            var node = (DebugInfoExpression)expr;
            int start = Instructions.Count;
            var info = new DebugInfo()
            {
                Index = start,
                FileName = node.Document.FileName,
                StartLine = node.StartLine,
                EndLine = node.EndLine,
                IsClear = node.IsClear
            };
            _debugInfos.Add(info);
        }

        private void CompileRuntimeVariablesExpression(Expression expr) {
            // Generates IRuntimeVariables for all requested variables
            var node = (RuntimeVariablesExpression)expr;
            foreach (var variable in node.Variables) {
                EnsureAvailableForClosure(variable);
                CompileGetBoxedVariable(variable);
            }

            Instructions.EmitNewRuntimeVariables(node.Variables.Count);
        }

        private void CompileLambdaExpression(Expression expr) {
            var node = (LambdaExpression)expr;
            var compiler = new LightCompiler(this);
            var creator = compiler.CompileTop(node);

            if (compiler._locals.ClosureVariables != null) {
                foreach (ParameterExpression variable in compiler._locals.ClosureVariables.Keys) {
                    CompileGetBoxedVariable(variable);
                }
            }
            Instructions.EmitCreateDelegate(creator);
        }

        private void CompileCoalesceBinaryExpression(Expression expr) {
            var node = (BinaryExpression)expr;

            if (TypeUtils.IsNullableType(node.Left.Type)) {
                throw new NotImplementedException();
            }

            if (node.Conversion != null) {
                throw new NotImplementedException();
            }

            var leftNotNull = Instructions.MakeLabel();
            Compile(node.Left);
            Instructions.EmitCoalescingBranch(leftNotNull);
            Instructions.EmitPop();
            Compile(node.Right);
            Instructions.MarkLabel(leftNotNull);
        }

        private void CompileInvocationExpression(Expression expr) {
            var node = (InvocationExpression)expr;

            // TODO: LambdaOperand optimization (see compiler)
            if (typeof(LambdaExpression).IsAssignableFrom(node.Expression.Type)) {
                throw new System.NotImplementedException();
            }

            // TODO: do not create a new Call Expression
            CompileMethodCallExpression(Expression.Call(node.Expression, node.Expression.Type.GetMethod("Invoke"), node.Arguments));
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "expr")]
        private void CompileListInitExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "expr")]
        private void CompileMemberInitExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "expr")]
        private void CompileQuoteUnaryExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        private void CompileUnboxUnaryExpression(Expression expr) {
            var node = (UnaryExpression)expr;
            // unboxing is a nop:
            Compile(node.Operand);
        }

        private void CompileTypeEqualExpression(Expression expr) {
            Debug.Assert(expr.NodeType == ExpressionType.TypeEqual);
            var node = (TypeBinaryExpression)expr;

            Compile(node.Expression);
            Instructions.EmitLoad(node.TypeOperand);
            Instructions.EmitTypeEquals();
        }

        private void CompileTypeAsExpression(UnaryExpression node) {
            Compile(node.Operand);
            Instructions.EmitTypeAs(node.Type);
        }

        private void CompileTypeIsExpression(Expression expr) {
            Debug.Assert(expr.NodeType == ExpressionType.TypeIs);
            var node = (TypeBinaryExpression)expr;

            Compile(node.Expression);

            // use TypeEqual for sealed types:
            if (node.TypeOperand.IsSealed) {
                Instructions.EmitLoad(node.TypeOperand);
                Instructions.EmitTypeEquals();
            } else {
                Instructions.EmitTypeIs(node.TypeOperand);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "expr")]
        private void CompileReducibleExpression(Expression expr) {
            throw new System.NotImplementedException();
        }

        internal void Compile(Expression expr, bool asVoid) {
            if (asVoid) {
                CompileAsVoid(expr);
            } else {
                Compile(expr);
            }
        }

        internal void CompileAsVoid(Expression expr) {
            bool pushLabelBlock = TryPushLabelBlock(expr);
            int startingStackDepth = Instructions.CurrentStackDepth;
            switch (expr.NodeType) {
                case ExpressionType.Assign:
                    CompileAssignBinaryExpression(expr, true);
                    break;

                case ExpressionType.Block:
                    CompileBlockExpression(expr, true);
                    break;

                case ExpressionType.Throw:
                    CompileThrowUnaryExpression(expr, true);
                    break;

                case ExpressionType.Constant:
                case ExpressionType.Default:
                case ExpressionType.Parameter:
                    // no-op
                    break;

                default:
                    CompileNoLabelPush(expr);
                    if (expr.Type != typeof(void)) {
                        Instructions.EmitPop();
                    }
                    break;
            }
            Debug.Assert(Instructions.CurrentStackDepth == startingStackDepth);
            if (pushLabelBlock) {
                PopLabelBlock(_labelBlock.Kind);
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        private void CompileNoLabelPush(Expression expr) {
            int startingStackDepth = Instructions.CurrentStackDepth;
            switch (expr.NodeType) {
                case ExpressionType.Add: CompileBinaryExpression(expr); break;
                case ExpressionType.AddChecked: CompileBinaryExpression(expr); break;
                case ExpressionType.And: CompileBinaryExpression(expr); break;
                case ExpressionType.AndAlso: CompileAndAlsoBinaryExpression(expr); break;
                case ExpressionType.ArrayLength: CompileUnaryExpression(expr); break;
                case ExpressionType.ArrayIndex: CompileBinaryExpression(expr); break;
                case ExpressionType.Call: CompileMethodCallExpression(expr); break;
                case ExpressionType.Coalesce: CompileCoalesceBinaryExpression(expr); break;
                case ExpressionType.Conditional: CompileConditionalExpression(expr, expr.Type == typeof(void)); break;
                case ExpressionType.Constant: CompileConstantExpression(expr); break;
                case ExpressionType.Convert: CompileConvertUnaryExpression(expr); break;
                case ExpressionType.ConvertChecked: CompileConvertUnaryExpression(expr); break;
                case ExpressionType.Divide: CompileBinaryExpression(expr); break;
                case ExpressionType.Equal: CompileBinaryExpression(expr); break;
                case ExpressionType.ExclusiveOr: CompileBinaryExpression(expr); break;
                case ExpressionType.GreaterThan: CompileBinaryExpression(expr); break;
                case ExpressionType.GreaterThanOrEqual: CompileBinaryExpression(expr); break;
                case ExpressionType.Invoke: CompileInvocationExpression(expr); break;
                case ExpressionType.Lambda: CompileLambdaExpression(expr); break;
                case ExpressionType.LeftShift: CompileBinaryExpression(expr); break;
                case ExpressionType.LessThan: CompileBinaryExpression(expr); break;
                case ExpressionType.LessThanOrEqual: CompileBinaryExpression(expr); break;
                case ExpressionType.ListInit: CompileListInitExpression(expr); break;
                case ExpressionType.MemberAccess: CompileMemberExpression(expr); break;
                case ExpressionType.MemberInit: CompileMemberInitExpression(expr); break;
                case ExpressionType.Modulo: CompileBinaryExpression(expr); break;
                case ExpressionType.Multiply: CompileBinaryExpression(expr); break;
                case ExpressionType.MultiplyChecked: CompileBinaryExpression(expr); break;
                case ExpressionType.Negate: CompileUnaryExpression(expr); break;
                case ExpressionType.UnaryPlus: CompileUnaryExpression(expr); break;
                case ExpressionType.NegateChecked: CompileUnaryExpression(expr); break;
                case ExpressionType.New: CompileNewExpression(expr); break;
                case ExpressionType.NewArrayInit: CompileNewArrayExpression(expr); break;
                case ExpressionType.NewArrayBounds: CompileNewArrayExpression(expr); break;
                case ExpressionType.Not: CompileUnaryExpression(expr); break;
                case ExpressionType.NotEqual: CompileBinaryExpression(expr); break;
                case ExpressionType.Or: CompileBinaryExpression(expr); break;
                case ExpressionType.OrElse: CompileOrElseBinaryExpression(expr); break;
                case ExpressionType.Parameter: CompileParameterExpression(expr); break;
                case ExpressionType.Power: CompileBinaryExpression(expr); break;
                case ExpressionType.Quote: CompileQuoteUnaryExpression(expr); break;
                case ExpressionType.RightShift: CompileBinaryExpression(expr); break;
                case ExpressionType.Subtract: CompileBinaryExpression(expr); break;
                case ExpressionType.SubtractChecked: CompileBinaryExpression(expr); break;
                case ExpressionType.TypeAs: CompileUnaryExpression(expr); break;
                case ExpressionType.TypeIs: CompileTypeIsExpression(expr); break;
                case ExpressionType.Assign: CompileAssignBinaryExpression(expr, expr.Type == typeof(void)); break;
                case ExpressionType.Block: CompileBlockExpression(expr, expr.Type == typeof(void)); break;
                case ExpressionType.DebugInfo: CompileDebugInfoExpression(expr); break;
                case ExpressionType.Decrement: CompileUnaryExpression(expr); break;
                case ExpressionType.Dynamic: CompileDynamicExpression(expr); break;
                case ExpressionType.Default: CompileDefaultExpression(expr); break;
                case ExpressionType.Extension: CompileExtensionExpression(expr); break;
                case ExpressionType.Goto: CompileGotoExpression(expr); break;
                case ExpressionType.Increment: CompileUnaryExpression(expr); break;
                case ExpressionType.Index: CompileIndexExpression(expr); break;
                case ExpressionType.Label: CompileLabelExpression(expr); break;
                case ExpressionType.RuntimeVariables: CompileRuntimeVariablesExpression(expr); break;
                case ExpressionType.Loop: CompileLoopExpression(expr); break;
                case ExpressionType.Switch: CompileSwitchExpression(expr); break;
                case ExpressionType.Throw: CompileThrowUnaryExpression(expr, expr.Type == typeof(void)); break;
                case ExpressionType.Try: CompileTryExpression(expr); break;
                case ExpressionType.Unbox: CompileUnboxUnaryExpression(expr); break;
                case ExpressionType.TypeEqual: CompileTypeEqualExpression(expr); break;
                case ExpressionType.OnesComplement: CompileUnaryExpression(expr); break;
                case ExpressionType.IsTrue: CompileUnaryExpression(expr); break;
                case ExpressionType.IsFalse: CompileUnaryExpression(expr); break;
                case ExpressionType.AddAssign:
                case ExpressionType.AndAssign:
                case ExpressionType.DivideAssign:
                case ExpressionType.ExclusiveOrAssign:
                case ExpressionType.LeftShiftAssign:
                case ExpressionType.ModuloAssign:
                case ExpressionType.MultiplyAssign:
                case ExpressionType.OrAssign:
                case ExpressionType.PowerAssign:
                case ExpressionType.RightShiftAssign:
                case ExpressionType.SubtractAssign:
                case ExpressionType.AddAssignChecked:
                case ExpressionType.MultiplyAssignChecked:
                case ExpressionType.SubtractAssignChecked:
                case ExpressionType.PreIncrementAssign:
                case ExpressionType.PreDecrementAssign:
                case ExpressionType.PostIncrementAssign:
                case ExpressionType.PostDecrementAssign:
                    CompileReducibleExpression(expr); break;
                default: throw Assert.Unreachable;
            }

            Debug.Assert(Instructions.CurrentStackDepth == startingStackDepth + (expr.Type == typeof(void) ? 0 : 1));
        }

        public void Compile(Expression expr) {
            bool pushLabelBlock = TryPushLabelBlock(expr);
            CompileNoLabelPush(expr);
            if (pushLabelBlock) {
                PopLabelBlock(_labelBlock.Kind);
            }
        }
    }
}
