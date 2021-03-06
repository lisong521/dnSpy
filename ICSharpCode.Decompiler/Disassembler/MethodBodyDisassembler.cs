﻿// Copyright (c) 2011 AlphaSierraPapa for the SharpDevelop Team
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy of this
// software and associated documentation files (the "Software"), to deal in the Software
// without restriction, including without limitation the rights to use, copy, modify, merge,
// publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
// to whom the Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all copies or
// substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
// PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
// FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
// OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.FlowAnalysis;
using ICSharpCode.Decompiler.ILAst;
using ICSharpCode.NRefactory;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace ICSharpCode.Decompiler.Disassembler
{
	/// <summary>
	/// Disassembles a method body.
	/// </summary>
	public sealed class MethodBodyDisassembler
	{
		readonly ITextOutput output;
		readonly bool detectControlStructure;
		readonly DisassemblerOptions options;
		
		public MethodBodyDisassembler(ITextOutput output, bool detectControlStructure, DisassemblerOptions options)
		{
			if (output == null)
				throw new ArgumentNullException("output");
			this.output = output;
			this.detectControlStructure = detectControlStructure;
			this.options = options;
		}
		
		public void Disassemble(MethodDef method, MemberMapping debugSymbols)
		{
			// start writing IL code
			CilBody body = method.Body;
			uint codeSize = (uint)body.GetCodeSize();
			output.WriteLine(string.Format("// RVA 0x{0:x8} - 0x{1:x8} ({2} (0x{2:x}) bytes)", (uint)method.RVA, (uint)method.RVA + codeSize, codeSize), TextTokenType.Comment);
			output.WriteLine(string.Format("// Metadata token 0x{0:x8} (RID {1})", method.MDToken.ToInt32(), method.Rid), TextTokenType.Comment);
			if (body.LocalVarSigTok != 0)
				output.WriteLine(string.Format("// LocalVarSig token 0x{0:x8} (RID {1})", body.LocalVarSigTok, body.LocalVarSigTok & 0xFFFFFF), TextTokenType.Comment);
			output.Write(".maxstack", TextTokenType.ILDirective);
			output.WriteSpace();
			output.WriteLine(string.Format("{0}", body.MaxStack), TextTokenType.Number);
            if (method.DeclaringType.Module.EntryPoint == method)
                output.WriteLine (".entrypoint", TextTokenType.ILDirective);
			
			if (method.Body.HasVariables) {
				output.Write(".locals", TextTokenType.ILDirective);
				output.WriteSpace();
				if (method.Body.InitLocals) {
					output.Write("init", TextTokenType.Keyword);
					output.WriteSpace();
				}
				output.WriteLine("(", TextTokenType.Operator);
				output.Indent();
				foreach (var v in method.Body.Variables) {
					output.Write('[', TextTokenType.Operator);
					output.WriteDefinition(v.Index.ToString(), v, TextTokenType.Number);
					output.Write(']', TextTokenType.Operator);
					output.WriteSpace();
					v.Type.WriteTo(output);
					if (!string.IsNullOrEmpty(v.Name)) {
						output.WriteSpace();
						output.Write(DisassemblerHelpers.Escape(v.Name), TextTokenType.Local);
					}
					if (v.Index + 1 < method.Body.Variables.Count)
						output.Write(',', TextTokenType.Operator);
					output.WriteLine();
				}
				output.Unindent();
				output.WriteLine(")", TextTokenType.Operator);
			}
			output.WriteLine();
			
			if (detectControlStructure && body.Instructions.Count > 0) {
				int index = 0;
				HashSet<uint> branchTargets = GetBranchTargets(body.Instructions);
				WriteStructureBody(body, new ILStructure(body), branchTargets, ref index, debugSymbols, method.Body.GetCodeSize());
			} else {
				var instructions = method.Body.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var inst = instructions[i];
					var startLocation = output.Location;
					inst.WriteTo(output, options.GetOpCodeDocumentation);
					
					if (debugSymbols != null) {
						// add IL code mappings - used in debugger
						var next = i + 1 < instructions.Count ? instructions[i + 1] : null;
						debugSymbols.MemberCodeMappings.Add(
							new SourceCodeMapping() {
								StartLocation = output.Location,
								EndLocation = output.Location,
								ILInstructionOffset = new ILRange(inst.Offset, next == null ? (uint)method.Body.GetCodeSize() : next.Offset),
								MemberMapping = debugSymbols
							});
					}
					
					output.WriteLine();
				}
				if (method.Body.HasExceptionHandlers) {
					output.WriteLine();
					foreach (var eh in method.Body.ExceptionHandlers) {
						eh.WriteTo(output);
						output.WriteLine();
					}
				}
			}
		}
		
		HashSet<uint> GetBranchTargets(IEnumerable<Instruction> instructions)
		{
			HashSet<uint> branchTargets = new HashSet<uint>();
			foreach (var inst in instructions) {
				Instruction target = inst.Operand as Instruction;
				if (target != null)
					branchTargets.Add(target.Offset);
				IList<Instruction> targets = inst.Operand as IList<Instruction>;
				if (targets != null)
					foreach (Instruction t in targets)
						if (t != null)
							branchTargets.Add(t.Offset);
			}
			return branchTargets;
		}
		
		void WriteStructureHeader(ILStructure s)
		{
			switch (s.Type) {
				case ILStructureType.Loop:
					output.Write("// loop start", TextTokenType.Comment);
					if (s.LoopEntryPoint != null) {
						output.Write(" (head: ", TextTokenType.Comment);
						DisassemblerHelpers.WriteOffsetReference(output, s.LoopEntryPoint, TextTokenType.Comment);
						output.Write(')', TextTokenType.Comment);
					}
					output.WriteLine();
					break;
				case ILStructureType.Try:
					output.WriteLine(".try", TextTokenType.ILDirective);
					output.WriteLineLeftBrace();
					break;
				case ILStructureType.Handler:
					switch (s.ExceptionHandler.HandlerType) {
						case ExceptionHandlerType.Catch:
						case ExceptionHandlerType.Filter:
							output.Write("catch", TextTokenType.Keyword);
							if (s.ExceptionHandler.CatchType != null) {
								output.WriteSpace();
								s.ExceptionHandler.CatchType.WriteTo(output, ILNameSyntax.TypeName);
							}
							output.WriteLine();
							break;
						case ExceptionHandlerType.Finally:
							output.WriteLine("finally", TextTokenType.Keyword);
							break;
						case ExceptionHandlerType.Fault:
							output.WriteLine("fault", TextTokenType.Keyword);
							break;
						default:
							output.WriteLine(s.ExceptionHandler.HandlerType.ToString(), TextTokenType.Keyword);
							break;
					}
					output.WriteLineLeftBrace();
					break;
				case ILStructureType.Filter:
					output.WriteLine("filter", TextTokenType.Keyword);
					output.WriteLineLeftBrace();
					break;
				default:
					throw new NotSupportedException();
			}
			output.Indent();
		}
		
		void WriteStructureBody(CilBody body, ILStructure s, HashSet<uint> branchTargets, ref int index, MemberMapping debugSymbols, int codeSize)
		{
			bool isFirstInstructionInStructure = true;
			bool prevInstructionWasBranch = false;
			int childIndex = 0;
			var instructions = body.Instructions;
			while (index < instructions.Count) {
				Instruction inst = instructions[index];
				if (inst.Offset >= s.EndOffset)
					break;
				uint offset = inst.Offset;
				if (childIndex < s.Children.Count && s.Children[childIndex].StartOffset <= offset && offset < s.Children[childIndex].EndOffset) {
					ILStructure child = s.Children[childIndex++];
					WriteStructureHeader(child);
					WriteStructureBody(body, child, branchTargets, ref index, debugSymbols, codeSize);
					WriteStructureFooter(child);
				} else {
					if (!isFirstInstructionInStructure && (prevInstructionWasBranch || branchTargets.Contains(offset))) {
						output.WriteLine(); // put an empty line after branches, and in front of branch targets
					}
					var startLocation = output.Location;
					inst.WriteTo(output, options.GetOpCodeDocumentation);
					
					// add IL code mappings - used in debugger
					if (debugSymbols != null) {
						var next = index + 1 < instructions.Count ? instructions[index + 1] : null;
						debugSymbols.MemberCodeMappings.Add(
							new SourceCodeMapping() {
								StartLocation = startLocation,
								EndLocation = output.Location,
								ILInstructionOffset = new ILRange(inst.Offset, next == null ? (uint)codeSize : next.Offset),
								MemberMapping = debugSymbols
							});
					}
					
					output.WriteLine();
					
					prevInstructionWasBranch = inst.OpCode.FlowControl == FlowControl.Branch
						|| inst.OpCode.FlowControl == FlowControl.Cond_Branch
						|| inst.OpCode.FlowControl == FlowControl.Return
						|| inst.OpCode.FlowControl == FlowControl.Throw;
					
					index++;
				}
				isFirstInstructionInStructure = false;
			}
		}
		
		void WriteStructureFooter(ILStructure s)
		{
			output.Unindent();
			switch (s.Type) {
				case ILStructureType.Loop:
					output.WriteLine("// end loop", TextTokenType.Comment);
					break;
				case ILStructureType.Try:
					output.WriteRightBrace();
					output.WriteSpace();
					output.WriteLine("// end .try", TextTokenType.Comment);
					break;
				case ILStructureType.Handler:
					output.WriteRightBrace();
					output.WriteSpace();
					output.WriteLine("// end handler", TextTokenType.Comment);
					break;
				case ILStructureType.Filter:
					output.WriteRightBrace();
					output.WriteSpace();
					output.WriteLine("// end filter", TextTokenType.Comment);
					break;
				default:
					throw new NotSupportedException();
			}
		}
	}
}
