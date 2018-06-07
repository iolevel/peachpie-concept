﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Devsense.PHP.Syntax;
using Devsense.PHP.Syntax.Ast;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis.Utilities;
using Roslyn.Utilities;

namespace Pchp.CodeAnalysis.Symbols
{
    /// <summary>
    /// Represents a file within the mudule as a CLR type.
    /// </summary>
    /// <remarks>
    /// namespace [DIR]{
    ///     class [FNAME] {
    ///         object [Main](){ ... }
    ///     }
    /// }</remarks>
    sealed partial class SourceFileSymbol : NamedTypeSymbol, ILambdaContainerSymbol, IPhpScriptTypeSymbol
    {
        readonly PhpCompilation _compilation;
        readonly PhpSyntaxTree _syntaxTree;

        readonly SourceGlobalMethodSymbol _mainMethod;

        BaseAttributeData _lazyScriptAttribute;

        /// <summary>
        /// List of functions declared within the file.
        /// </summary>
        public ImmutableArray<SourceFunctionSymbol> Functions => _lazyMembers.OfType<SourceFunctionSymbol>().ToImmutableArray();

        /// <summary>
        /// List of types declared within the file.
        /// </summary>
        public List<SourceTypeSymbol> ContainedTypes
        {
            get { return _containedTypes; }
        }

        readonly List<Symbol> _lazyMembers = new List<Symbol>();
        readonly List<SourceTypeSymbol> _containedTypes = new List<SourceTypeSymbol>();

        public PhpSyntaxTree SyntaxTree => _syntaxTree;

        public SourceModuleSymbol SourceModule => _compilation.SourceModule;

        public SourceFileSymbol(PhpCompilation compilation, PhpSyntaxTree syntaxTree)
        {
            Contract.ThrowIfNull(compilation);
            Contract.ThrowIfNull(syntaxTree);

            _compilation = compilation;
            _syntaxTree = syntaxTree;
            _mainMethod = new SourceGlobalMethodSymbol(this);
        }

        /// <summary>
        /// Special main method representing the script global code.
        /// </summary>
        public IMethodSymbol MainMethod => _mainMethod;

        /// <summary>
        /// Lazily adds a function into the list of global functions declared within this file.
        /// </summary>
        internal void AddFunction(SourceFunctionSymbol routine)
        {
            Contract.ThrowIfNull(routine);
            _lazyMembers.Add(routine);
        }

        void ILambdaContainerSymbol.AddLambda(SourceLambdaSymbol routine)
        {
            Contract.ThrowIfNull(routine);
            _lazyMembers.Add(routine);
        }

        IEnumerable<SourceLambdaSymbol> ILambdaContainerSymbol.Lambdas
        {
            get
            {
                return _lazyMembers.OfType<SourceLambdaSymbol>();
            }
        }

        SourceLambdaSymbol ILambdaContainerSymbol.ResolveLambdaSymbol(LambdaFunctionExpr expr)
        {
            if (expr == null) throw new ArgumentNullException(nameof(expr));
            return _lazyMembers.OfType<SourceLambdaSymbol>().First(s => s.Syntax == expr);
        }

        /// <summary>
        /// Collects declaration diagnostics.
        /// </summary>
        internal void GetDiagnostics(DiagnosticBag diagnostic)
        {
            // check functions duplicity
            var funcs = this.Functions;
            if (funcs.Length > 1)
            {
                var set = new HashSet<QualifiedName>();

                // handle unconditionally declared functions:
                foreach (var f in funcs)
                {
                    if (f.IsConditional)
                    {
                        continue;
                    }

                    if (!set.Add(f.QualifiedName))
                    {
                        diagnostic.Add(DiagnosticBagExtensions.ParserDiagnostic(_syntaxTree, ((FunctionDecl)f.Syntax).HeadingSpan, Devsense.PHP.Errors.FatalErrors.FunctionRedeclared, f.QualifiedName.ToString()));
                    }
                }

                //// handle conditionally declared functions: // NOTE: commented since we should allow to compile it
                //foreach (var f in funcs.Where(f => f.IsConditional))
                //{
                //    if (set.Contains(f.QualifiedName))  // does not make sense to declare function if it is declared already unconditionally
                //    {
                //        diagnostic.Add(DiagnosticBagExtensions.ParserDiagnostic(_syntaxTree, ((FunctionDecl)f.Syntax).HeadingSpan, Devsense.PHP.Errors.FatalErrors.FunctionRedeclared, f.PhpName));
                //    }
                //}
            }

            // check class/interface duplicity:
            var types = this.ContainedTypes;
            if (types.Count > 1)
            {
                var set = new HashSet<QualifiedName>();
                foreach (var t in types)
                {
                    if (t.Syntax.IsConditional)
                    {
                        continue;
                    }

                    if (!set.Add(t.FullName))
                    {
                        diagnostic.Add(DiagnosticBagExtensions.ParserDiagnostic(_syntaxTree, t.Syntax.HeadingSpan, Devsense.PHP.Errors.FatalErrors.TypeRedeclared, t.FullName.ToString()));
                    }
                }
            }
        }

        public string RelativeFilePath =>
            PhpFileUtilities.GetRelativePath(
                PhpFileUtilities.NormalizeSlashes(_syntaxTree.Source.FilePath),
                PhpFileUtilities.NormalizeSlashes(_compilation.Options.BaseDirectory));

        /// <summary>
        /// Gets relative path excluding the file name and trailing slashes.
        /// </summary>
        internal string DirectoryRelativePath
        {
            get
            {
                return (PathUtilities.GetDirectoryName(this.RelativeFilePath) ?? string.Empty)
                    .TrimEnd(PathUtilities.AltDirectorySeparatorChar);     // NormalizeRelativeDirectoryPath
            }
        }

        public override string Name => PathUtilities.GetFileName(_syntaxTree.Source.FilePath, true).Replace('.', '_');

        public override string NamespaceName
        {
            get
            {
                return WellKnownPchpNames.ScriptsRootNamespace + DirectoryRelativePath;
            }
        }

        public override ImmutableArray<AttributeData> GetAttributes()
        {
            // [ScriptAttribute(RelativeFilePath)]  // TODO: LastWriteTime
            if (_lazyScriptAttribute == null)
            {
                _lazyScriptAttribute = new SynthesizedAttributeData(
                    DeclaringCompilation.CoreMethods.Ctors.ScriptAttribute_string,
                    ImmutableArray.Create(new TypedConstant(DeclaringCompilation.CoreTypes.String.Symbol, TypedConstantKind.Primitive, this.RelativeFilePath)),
                    ImmutableArray<KeyValuePair<string, TypedConstant>>.Empty);
            }

            //
            return ImmutableArray.Create<AttributeData>(_lazyScriptAttribute);
        }

        public override NamedTypeSymbol BaseType
        {
            get
            {
                return _compilation.CoreTypes.Object;
            }
        }

        public override int Arity => 0;

        internal override bool HasTypeArgumentsCustomModifiers => false;

        public override ImmutableArray<CustomModifier> GetTypeArgumentCustomModifiers(int ordinal) => GetEmptyTypeArgumentCustomModifiers(ordinal);

        public override Symbol ContainingSymbol => _compilation.SourceModule;

        internal override IModuleSymbol ContainingModule => _compilation.SourceModule;

        internal override PhpCompilation DeclaringCompilation => _compilation;

        public override Accessibility DeclaredAccessibility => Accessibility.Public;

        public override ImmutableArray<SyntaxReference> DeclaringSyntaxReferences
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        internal override bool IsInterface => false;

        public override bool IsAbstract => false;

        public override bool IsSealed => true;

        public override bool IsStatic => false;

        public override ImmutableArray<Location> Locations
        {
            get
            {
                return ImmutableArray.Create(Location.Create(SyntaxTree, default(Microsoft.CodeAnalysis.Text.TextSpan)));
            }
        }

        public override TypeKind TypeKind => TypeKind.Class;

        internal override bool ShouldAddWinRTMembers => false;

        internal override bool IsWindowsRuntimeImport => false;

        internal override TypeLayout Layout => default(TypeLayout);

        internal override bool MangleName => false;

        internal override ObsoleteAttributeData ObsoleteAttributeData => null;

        internal override ImmutableArray<NamedTypeSymbol> GetDeclaredInterfaces(ConsList<Symbol> basesBeingResolved)
        {
            return ImmutableArray<NamedTypeSymbol>.Empty;
        }

        public override ImmutableArray<Symbol> GetMembers()
        {
            var builder = ImmutableArray.CreateBuilder<Symbol>(1 + _lazyMembers.Count);

            builder.Add(_mainMethod);
            builder.AddRange(_lazyMembers);

            return builder.ToImmutable();
        }

        public override ImmutableArray<Symbol> GetMembers(string name, bool ignoreCase = false) => GetMembers().Where(x => x.Name.StringsEqual(name, ignoreCase)).ToImmutableArray();

        public override ImmutableArray<MethodSymbol> StaticConstructors => ImmutableArray<MethodSymbol>.Empty;

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers() => _lazyMembers.OfType<NamedTypeSymbol>().AsImmutable();

        public override ImmutableArray<NamedTypeSymbol> GetTypeMembers(string name) => _lazyMembers.OfType<NamedTypeSymbol>().Where(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).AsImmutable();

        internal override IEnumerable<IFieldSymbol> GetFieldsToEmit() => GetMembers().OfType<FieldSymbol>();

        internal override ImmutableArray<NamedTypeSymbol> GetInterfacesToEmit() => ImmutableArray<NamedTypeSymbol>.Empty;
    }
}
