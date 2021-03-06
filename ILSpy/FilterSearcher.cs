﻿/*
    Copyright (C) 2014-2015 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Media;
using ICSharpCode.ILSpy.TreeNodes;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace ICSharpCode.ILSpy
{
	interface ISearchComparer
	{
		/// <summary>
		/// Checks whether some value matches something
		/// </summary>
		/// <param name="text">String representation of <paramref name="obj"/> or null</param>
		/// <param name="obj">Original object</param>
		/// <returns></returns>
		bool IsMatch(string text, object obj);
	}

	sealed class RegExStringLiteralSearchComparer : ISearchComparer
	{
		readonly Regex regex;

		public RegExStringLiteralSearchComparer(Regex regex)
		{
			if (regex == null)
				throw new ArgumentNullException();
			this.regex = regex;
		}

		public bool IsMatch(string text, object obj)
		{
			var hc = obj as IHasConstant;
			if (hc != null && hc.Constant != null)
				obj = hc.Constant.Value;

			text = obj as string;
			return text != null && regex.IsMatch(text);
		}
	}

	sealed class StringLiteralSearchComparer : ISearchComparer
	{
		readonly string str;
		readonly StringComparison stringComparison;
		readonly bool matchWholeString;

		public StringLiteralSearchComparer(string s, bool caseSensitive = false, bool matchWholeString = false)
		{
			if (s == null)
				throw new ArgumentNullException();
			this.str = s;
			this.stringComparison = caseSensitive ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase;
			this.matchWholeString = matchWholeString;
		}

		public bool IsMatch(string text, object obj)
		{
			var hc = obj as IHasConstant;
			if (hc != null && hc.Constant != null)
				obj = hc.Constant.Value;

			text = obj as string;
			if (text == null)
				return false;
			if (matchWholeString)
				return text.Equals(str, stringComparison);
			return text.IndexOf(str, stringComparison) >= 0;
		}
	}

	sealed class IntegerLiteralSearchComparer : ISearchComparer
	{
		readonly long searchValue;

		public IntegerLiteralSearchComparer(long value)
		{
			this.searchValue = value;
		}

		public bool IsMatch(string text, object obj)
		{
			var hc = obj as IHasConstant;
			if (hc != null && hc.Constant != null)
				obj = hc.Constant.Value;
			if (obj == null)
				return false;

			switch (Type.GetTypeCode(obj.GetType())) {
			case TypeCode.SByte:	return searchValue == (sbyte)obj;
			case TypeCode.Byte:		return searchValue == (byte)obj;
			case TypeCode.Int16:	return searchValue == (short)obj;
			case TypeCode.UInt16:	return searchValue == (ushort)obj;
			case TypeCode.Int32:	return searchValue == (int)obj;
			case TypeCode.UInt32:	return searchValue == (uint)obj;
			case TypeCode.Int64:	return searchValue == (long)obj;
			case TypeCode.UInt64:	return searchValue == unchecked((long)(ulong)obj);
			case TypeCode.Single:	return searchValue == (float)obj;
			case TypeCode.Double:	return searchValue == (double)obj;
			}

			return false;
		}
	}

	sealed class DoubleLiteralSearchComparer : ISearchComparer
	{
		readonly double searchValue;

		public DoubleLiteralSearchComparer(double value)
		{
			this.searchValue = value;
		}

		public bool IsMatch(string text, object obj)
		{
			var hc = obj as IHasConstant;
			if (hc != null && hc.Constant != null)
				obj = hc.Constant.Value;
			if (obj == null)
				return false;

			switch (Type.GetTypeCode(obj.GetType())) {
			case TypeCode.SByte:	return searchValue == (sbyte)obj;
			case TypeCode.Byte:		return searchValue == (byte)obj;
			case TypeCode.Int16:	return searchValue == (short)obj;
			case TypeCode.UInt16:	return searchValue == (ushort)obj;
			case TypeCode.Int32:	return searchValue == (int)obj;
			case TypeCode.UInt32:	return searchValue == (uint)obj;
			case TypeCode.Int64:	return searchValue == (long)obj;
			case TypeCode.UInt64:	return searchValue == (ulong)obj;
			case TypeCode.Single:	return searchValue == (float)obj;
			case TypeCode.Double:	return searchValue == (double)obj;
			}

			return false;
		}
	}

	sealed class RegExSearchComparer : ISearchComparer
	{
		readonly Regex regex;

		public RegExSearchComparer(Regex regex)
		{
			if (regex == null)
				throw new ArgumentNullException();
			this.regex = regex;
		}

		public bool IsMatch(string text, object obj)
		{
			if (text == null)
				return false;
			return regex.IsMatch(text);
		}
	}

	sealed class AndSearchComparer : ISearchComparer
	{
		readonly string[] searchTerms;
		readonly StringComparison stringComparison;
		readonly bool matchWholeWords;

		public AndSearchComparer(string[] searchTerms, bool caseSensitive = false, bool matchWholeWords = false)
			: this(searchTerms, caseSensitive ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase, matchWholeWords)
		{
		}

		public AndSearchComparer(string[] searchTerms, StringComparison stringComparison, bool matchWholeWords = false)
		{
			this.searchTerms = searchTerms;
			this.stringComparison = stringComparison;
			this.matchWholeWords = matchWholeWords;
		}

		public bool IsMatch(string text, object obj)
		{
			if (text == null)
				return false;
			foreach (var searchTerm in searchTerms) {
				if (matchWholeWords) {
					if (!text.Equals(searchTerm, stringComparison))
						return false;
				}
				else {
					if (text.IndexOf(searchTerm, stringComparison) < 0)
						return false;
				}
			}

			return true;
		}
	}

	sealed class OrSearchComparer : ISearchComparer
	{
		readonly string[] searchTerms;
		readonly StringComparison stringComparison;
		readonly bool matchWholeWords;

		public OrSearchComparer(string[] searchTerms, bool caseSensitive = false, bool matchWholeWords = false)
			: this(searchTerms, caseSensitive ? StringComparison.InvariantCulture : StringComparison.InvariantCultureIgnoreCase, matchWholeWords)
		{
		}

		public OrSearchComparer(string[] searchTerms, StringComparison stringComparison, bool matchWholeWords = false)
		{
			this.searchTerms = searchTerms;
			this.stringComparison = stringComparison;
			this.matchWholeWords = matchWholeWords;
		}

		public bool IsMatch(string text, object obj)
		{
			if (text == null)
				return false;
			foreach (var searchTerm in searchTerms) {
				if (matchWholeWords) {
					if (text.Equals(searchTerm, stringComparison))
						return true;
				}
				else {
					if (text.IndexOf(searchTerm, stringComparison) >= 0)
						return true;
				}
			}

			return false;
		}
	}

	/// <summary>
	/// Searches types/members/etc for text. A filter decides which type/member/etc to check.
	/// </summary>
	sealed class FilterSearcher
	{
		readonly ITreeViewNodeFilter filter;
		readonly ISearchComparer searchComparer;
		readonly Action<SearchResult> onMatch;
		readonly Language language;
		CancellationToken cancellationToken;

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="filter">Filter</param>
		/// <param name="searchComparer">Search comparer</param>
		/// <param name="onMatch">Called when there's a match</param>
		/// <param name="language">Language</param>
		/// <param name="cancellationToken">Cancellation token</param>
		public FilterSearcher(ITreeViewNodeFilter filter, ISearchComparer searchComparer, Action<SearchResult> onMatch, Language language, CancellationToken cancellationToken)
		{
			if (filter == null)
				throw new ArgumentNullException();
			if (searchComparer == null)
				throw new ArgumentNullException();
			if (onMatch == null)
				throw new ArgumentNullException();
			if (language == null)
				throw new ArgumentNullException();
			this.filter = filter;
			this.searchComparer = searchComparer;
			this.onMatch = onMatch;
			this.language = language;
			this.cancellationToken = cancellationToken;
		}

		bool IsMatch(string text, object obj)
		{
			return searchComparer.IsMatch(text, obj);
		}

		/// <summary>
		/// Search the assemblies and netmodules.
		/// </summary>
		/// <param name="asmNodes">Assembly and/or netmodule nodes</param>
		public void SearchAssemblies(IEnumerable<AssemblyTreeNode> asmNodes)
		{
			foreach (var asmNode in asmNodes) {
				cancellationToken.ThrowIfCancellationRequested();
				if (asmNode.LoadedAssembly.AssemblyDefinition != null)
					SearchAssemblyInternal(asmNode);
				else
					SearchModule(asmNode.LoadedAssembly);
			}
		}

		void SearchAssemblyInternal(AssemblyTreeNode asmNode)
		{
			if (asmNode == null)
				return;
			var res = filter.GetFilterResult(asmNode.LoadedAssembly, AssemblyFilterType.Assembly);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(asmNode.LoadedAssembly.AssemblyDefinition.FullName, asmNode.LoadedAssembly)) {
				onMatch(new SearchResult {
					Language = language,
					Object = asmNode,
					NameObject = asmNode.LoadedAssembly.AssemblyDefinition,
					TypeImageInfo = GetAssemblyImage(asmNode.LoadedAssembly.ModuleDefinition),
					LocationObject = null,
					LocationImageInfo = new ImageInfo(),
					LoadedAssembly = asmNode.LoadedAssembly,
				});
			}

			Debug.Assert(!asmNode.LazyLoading);
			if (asmNode.LazyLoading)
				throw new InvalidOperationException("Assembly's children haven't been loaded yet. Load them in the UI thread.");
			foreach (AssemblyTreeNode modNode in asmNode.Children) {
				cancellationToken.ThrowIfCancellationRequested();
				SearchModule(modNode.LoadedAssembly);
			}
		}

		static ImageInfo GetAssemblyImage(ModuleDef module)
		{
			if (module == null)
				return new ImageInfo();
			return (module.Characteristics & dnlib.PE.Characteristics.Dll) == 0 ?
				GetImage("AssemblyExe") : GetImage("Assembly");
		}

		static ImageInfo GetImage(string name)
		{
			return new ImageInfo(name, BackgroundType.Search);
		}

		void SearchModule(LoadedAssembly module)
		{
			if (module == null)
				return;
			var mod = module.ModuleDefinition;
			if (mod == null) {
				SearchNonNetFile(module);
				return;
			}

			var res = filter.GetFilterResult(module, AssemblyFilterType.NetModule);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(mod.FullName, module)) {
				onMatch(new SearchResult {
					Language = language,
					Object = module,
					NameObject = mod,
					TypeImageInfo = GetImage("AssemblyModule"),
					LocationObject = mod.Assembly != null ? mod.Assembly : null,
					LocationImageInfo = mod.Assembly != null ? GetAssemblyImage(mod.Assembly.ManifestModule) : new ImageInfo(),
					LoadedAssembly = module,
				});
			}

			SearchModAsmReferences(module);

			foreach (var kv in GetNamespaces(mod)) {
				cancellationToken.ThrowIfCancellationRequested();
				Search(module, kv.Key, kv.Value);
			}
		}

		void SearchModAsmReferences(LoadedAssembly module)
		{
			var mod = module.ModuleDefinition as ModuleDefMD;
			if (mod == null)
				return;

			var res = filter.GetFilterResult((ReferenceFolderTreeNode)null);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			foreach (var asmRef in mod.GetAssemblyRefs()) {
				res = filter.GetFilterResult(asmRef);
				if (res.FilterResult == FilterResult.Hidden)
					continue;

				if (res.IsMatch && IsMatch(asmRef.FullName, asmRef)) {
					onMatch(new SearchResult {
						Language = language,
						Object = asmRef,
						NameObject = asmRef,
						TypeImageInfo = GetImage("AssemblyReference"),
						LocationObject = module.ModuleDefinition,
						LocationImageInfo = GetImage("AssemblyModule"),
						LoadedAssembly = module,
					});
				}
			}

			foreach (var modRef in mod.GetModuleRefs()) {
				res = filter.GetFilterResult(modRef);
				if (res.FilterResult == FilterResult.Hidden)
					continue;

				if (res.IsMatch && IsMatch(modRef.FullName, modRef)) {
					onMatch(new SearchResult {
						Language = language,
						Object = modRef,
						NameObject = modRef,
						TypeImageInfo = GetImage("ModuleReference"),
						LocationObject = module.ModuleDefinition,
						LocationImageInfo = GetImage("AssemblyModule"),
						LoadedAssembly = module,
					});
				}
			}
		}

		Dictionary<string, List<TypeDef>> GetNamespaces(ModuleDef module)
		{
			var ns = new Dictionary<string, List<TypeDef>>(StringComparer.Ordinal);

			foreach (var type in module.Types) {
				List<TypeDef> list;
				if (!ns.TryGetValue(type.Namespace, out list))
					ns.Add(type.Namespace, list = new List<TypeDef>());
				list.Add(type);
			}

			return ns;
		}

		void SearchNonNetFile(LoadedAssembly nonNetFile)
		{
			if (nonNetFile == null)
				return;
			var res = filter.GetFilterResult(nonNetFile, AssemblyFilterType.NonNetFile);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(nonNetFile.ShortName, nonNetFile)) {
				onMatch(new SearchResult {
					Language = language,
					Object = nonNetFile,
					NameObject = nonNetFile,
					TypeImageInfo = GetImage("AssemblyWarning"),
					LocationObject = null,
					LocationImageInfo = new ImageInfo(),
					LoadedAssembly = nonNetFile,
				});
			}
		}

		void Search(LoadedAssembly ownerModule, string ns, List<TypeDef> types)
		{
			var res = filter.GetFilterResult(ns, ownerModule);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(ns, ns)) {
				onMatch(new SearchResult {
					Language = language,
					Object = ns,
					NameObject = new NamespaceSearchResult(ns),
					TypeImageInfo = GetImage("Namespace"),
					LocationObject = ownerModule.ModuleDefinition,
					LocationImageInfo = GetImage("AssemblyModule"),
					LoadedAssembly = ownerModule,
				});
			}

			foreach (var type in types) {
				cancellationToken.ThrowIfCancellationRequested();
				Search(ownerModule, ns, type);
			}
		}

		void Search(LoadedAssembly ownerModule, string nsOwner, TypeDef type)
		{
			var res = filter.GetFilterResult(type);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && (IsMatch(type.FullName, type) || IsMatch(type.Name, type))) {
				onMatch(new SearchResult {
					Language = language,
					Object = type,
					NameObject = type,
					TypeImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
					LocationObject = new NamespaceSearchResult(nsOwner),
					LocationImageInfo = GetImage("Namespace"),
					LoadedAssembly = ownerModule,
				});
			}

			SearchMembers(ownerModule, type);

			foreach (var subType in type.GetTypes()) {
				cancellationToken.ThrowIfCancellationRequested();
				Search(ownerModule, subType);
			}
		}

		void Search(LoadedAssembly ownerModule, TypeDef type)
		{
			var res = filter.GetFilterResult(type);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && (IsMatch(type.FullName, type) || IsMatch(type.Name, type))) {
				onMatch(new SearchResult {
					Language = language,
					Object = type,
					NameObject = type,
					TypeImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
					LocationObject = type.DeclaringType,
					LocationImageInfo = TypeTreeNode.GetImageInfo(type.DeclaringType, BackgroundType.Search),
					LoadedAssembly = ownerModule,
				});
			}

			SearchMembers(ownerModule, type);
		}

		void SearchMembers(LoadedAssembly ownerModule, TypeDef type)
		{
			foreach (var method in type.Methods)
				Search(ownerModule, type, method);
			cancellationToken.ThrowIfCancellationRequested();
			foreach (var field in type.Fields)
				Search(ownerModule, type, field);
			cancellationToken.ThrowIfCancellationRequested();
			foreach (var prop in type.Properties)
				Search(ownerModule, type, prop);
			cancellationToken.ThrowIfCancellationRequested();
			foreach (var evt in type.Events)
				Search(ownerModule, type, evt);
		}

		void Search(LoadedAssembly ownerModule, TypeDef type, MethodDef method)
		{
			var res = filter.GetFilterResult(method);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(method.Name, method)) {
				onMatch(new SearchResult {
					Language = language,
					Object = method,
					NameObject = method,
					TypeImageInfo = MethodTreeNode.GetImageInfo(method, BackgroundType.Search),
					LocationObject = type,
					LocationImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
					LoadedAssembly = ownerModule,
				});
				return;
			}

			res = filter.GetFilterResultParamDefs(method);
			if (res.FilterResult != FilterResult.Hidden) {
				foreach (var pd in method.ParamDefs) {
					res = filter.GetFilterResult(method, pd);
					if (res.FilterResult == FilterResult.Hidden)
						continue;
					if (res.IsMatch && IsMatch(pd.Name, pd)) {
						onMatch(new SearchResult {
							Language = language,
							Object = method,
							NameObject = method,
							TypeImageInfo = MethodTreeNode.GetImageInfo(method, BackgroundType.Search),
							LocationObject = type,
							LocationImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
							LoadedAssembly = ownerModule,
						});
						return;
					}
				}
			}

			SearchBody(ownerModule, type, method);
		}

		void SearchBody(LoadedAssembly ownerModule, TypeDef type, MethodDef method)
		{
			bool loadedBody;
			SearchBody(ownerModule, type, method, out loadedBody);
			if (loadedBody)
				ICSharpCode.ILSpy.TreeNodes.Analyzer.Helpers.FreeMethodBody(method);
		}

		void SearchBody(LoadedAssembly ownerModule, TypeDef type, MethodDef method, out bool loadedBody)
		{
			loadedBody = false;
			CilBody body;

			var res = filter.GetFilterResultLocals(method);
			if (res.FilterResult != FilterResult.Hidden) {
				body = method.Body;
				if (body == null)
					return; // Return immediately. All code here depends on a non-null body
				loadedBody = true;

				foreach (var local in body.Variables) {
					res = filter.GetFilterResult(method, local);
					if (res.FilterResult == FilterResult.Hidden)
						continue;
					if (res.IsMatch && IsMatch(local.Name, local)) {
						onMatch(new SearchResult {
							Language = language,
							Object = method,
							NameObject = method,
							TypeImageInfo = MethodTreeNode.GetImageInfo(method, BackgroundType.Search),
							LocationObject = type,
							LocationImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
							LoadedAssembly = ownerModule,
						});
						return;
					}
				}
			}

			res = filter.GetFilterResultBody(method);
			if (res.FilterResult == FilterResult.Hidden)
				return;
			if (!res.IsMatch)
				return;

			body = method.Body;
			if (body == null)
				return; // Return immediately. All code here depends on a non-null body
			loadedBody = true;
			foreach (var instr in body.Instructions) {
				object operand;
				// Only check numbers and strings. Don't pass in any type of operand to IsMatch()
				switch (instr.OpCode.Code) {
				case Code.Ldc_I4_M1: operand = -1; break;
				case Code.Ldc_I4_0: operand = 0; break;
				case Code.Ldc_I4_1: operand = 1; break;
				case Code.Ldc_I4_2: operand = 2; break;
				case Code.Ldc_I4_3: operand = 3; break;
				case Code.Ldc_I4_4: operand = 4; break;
				case Code.Ldc_I4_5: operand = 5; break;
				case Code.Ldc_I4_6: operand = 6; break;
				case Code.Ldc_I4_7: operand = 7; break;
				case Code.Ldc_I4_8: operand = 8; break;
				case Code.Ldc_I4:
				case Code.Ldc_I4_S:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldstr: operand = instr.Operand; break;
				default: operand = null; break;
				}
				if (operand != null && IsMatch(null, operand)) {
					onMatch(new SearchResult {
						Language = language,
						Object = method,
						NameObject = method,
						TypeImageInfo = MethodTreeNode.GetImageInfo(method, BackgroundType.Search),
						LocationObject = type,
						LocationImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
						LoadedAssembly = ownerModule,
					});
					break;
				}
			}
		}

		void Search(LoadedAssembly ownerModule, TypeDef type, FieldDef field)
		{
			var res = filter.GetFilterResult(field);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(field.Name, field)) {
				onMatch(new SearchResult {
					Language = language,
					Object = field,
					NameObject = field,
					TypeImageInfo = FieldTreeNode.GetImageInfo(field, BackgroundType.Search),
					LocationObject = type,
					LocationImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
					LoadedAssembly = ownerModule,
				});
			}
		}

		void Search(LoadedAssembly ownerModule, TypeDef type, PropertyDef prop)
		{
			var res = filter.GetFilterResult(prop);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(prop.Name, prop)) {
				onMatch(new SearchResult {
					Language = language,
					Object = prop,
					NameObject = prop,
					TypeImageInfo = PropertyTreeNode.GetImageInfo(prop, BackgroundType.Search),
					LocationObject = type,
					LocationImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
					LoadedAssembly = ownerModule,
				});
			}
		}

		void Search(LoadedAssembly ownerModule, TypeDef type, EventDef evt)
		{
			var res = filter.GetFilterResult(evt);
			if (res.FilterResult == FilterResult.Hidden)
				return;

			if (res.IsMatch && IsMatch(evt.Name, evt)) {
				onMatch(new SearchResult {
					Language = language,
					Object = evt,
					NameObject = evt,
					TypeImageInfo = EventTreeNode.GetImageInfo(evt, BackgroundType.Search),
					LocationObject = type,
					LocationImageInfo = TypeTreeNode.GetImageInfo(type, BackgroundType.Search),
					LoadedAssembly = ownerModule,
				});
			}
		}
	}
}
