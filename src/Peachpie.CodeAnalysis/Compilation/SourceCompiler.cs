﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Devsense.PHP.Syntax;
using Microsoft.CodeAnalysis;
using Pchp.CodeAnalysis.CodeGen;
using Pchp.CodeAnalysis.Emit;
using Pchp.CodeAnalysis.FlowAnalysis;
using Pchp.CodeAnalysis.Semantics;
using Pchp.CodeAnalysis.Semantics.Graph;
using Pchp.CodeAnalysis.Symbols;

namespace Pchp.CodeAnalysis
{
    /// <summary>
    /// Performs compilation of all source methods.
    /// </summary>
    internal class SourceCompiler
    {
        readonly PhpCompilation _compilation;
        readonly PEModuleBuilder _moduleBuilder;
        readonly bool _emittingPdb;
        readonly DiagnosticBag _diagnostics;
        readonly CancellationToken _cancellationToken;

        readonly Worklist<BoundBlock> _worklist;

        public bool ConcurrentBuild => _compilation.Options.ConcurrentBuild;

        private SourceCompiler(PhpCompilation compilation, PEModuleBuilder moduleBuilder, bool emittingPdb, DiagnosticBag diagnostics, CancellationToken cancellationToken)
        {
            Contract.ThrowIfNull(compilation);
            Contract.ThrowIfNull(diagnostics);

            _compilation = compilation;
            _moduleBuilder = moduleBuilder;
            _emittingPdb = emittingPdb;
            _diagnostics = diagnostics;
            _cancellationToken = cancellationToken;

            // parallel worklist algorithm
            _worklist = new Worklist<BoundBlock>(AnalyzeBlock);

            // semantic model
        }

        void WalkMethods(Action<SourceRoutineSymbol> action, bool allowParallel = false)
        {
            var routines = _compilation.SourceSymbolCollection.AllRoutines;

            if (ConcurrentBuild && allowParallel)
            {
                Parallel.ForEach(routines, action);
            }
            else
            {
                routines.ForEach(action);
            }
        }

        void WalkTypes(Action<SourceTypeSymbol> action, bool allowParallel = false)
        {
            var types = _compilation.SourceSymbolCollection.GetTypes();

            if (ConcurrentBuild && allowParallel)
            {
                Parallel.ForEach(types, action);
            }
            else
            {
                types.ForEach(action);
            }
        }

        /// <summary>
        /// Enqueues routine's start block for analysis.
        /// </summary>
        void EnqueueRoutine(SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(routine);

            // lazily binds CFG and
            // adds their entry block to the worklist

            // TODO: reset LocalsTable, FlowContext and CFG

            _worklist.Enqueue(routine.ControlFlowGraph?.Start);

            // enqueue routine parameter default values
            routine.Parameters.OfType<SourceParameterSymbol>().Foreach(p =>
            {
                if (p.Initializer != null)
                {
                    EnqueueExpression(p.Initializer, routine.TypeRefContext, routine.GetNamingContext());
                }
            });
        }

        /// <summary>
        /// Enqueues the standalone expression for analysis.
        /// </summary>
        void EnqueueExpression(BoundExpression expression, TypeRefContext/*!*/ctx, NamingContext naming)
        {
            Contract.ThrowIfNull(expression);
            Contract.ThrowIfNull(ctx);

            var dummy = new BoundBlock()
            {
                FlowState = new FlowState(new FlowContext(ctx, null)),
            };

            dummy.Add(new BoundExpressionStatement(expression));

            _worklist.Enqueue(dummy);
        }

        /// <summary>
        /// Enqueues initializers of a class fields and constants.
        /// </summary>
        void EnqueueFieldsInitializer(SourceTypeSymbol type)
        {
            type.GetDeclaredMembers().OfType<SourceFieldSymbol>().Foreach(f =>
            {
                if (f.Initializer != null)
                {
                    EnqueueExpression(
                        f.Initializer,
                        TypeRefFactory.CreateTypeRefContext(type), //the context will be lost, analysis resolves constant values only and types are temporary
                        NameUtils.GetNamingContext(type.Syntax));
                }
            });
        }

        internal void ReanalyzeMethods()
        {
            this.WalkMethods(routine => _worklist.Enqueue(routine.ControlFlowGraph.Start));
        }

        internal void AnalyzeMethods()
        {
            // _worklist.AddAnalysis:

            // TypeAnalysis + ResolveSymbols
            // LowerBody(block)

            // analyse blocks
            _worklist.DoAll(concurrent: false/*TODO: ConcurrentBuild*/);
        }

        void AnalyzeBlock(BoundBlock block) // TODO: driver
        {
            // TODO: pool of CFGAnalysis
            // TODO: async
            // TODO: in parallel

            block.Accept(AnalysisFactory(block.FlowState));
        }

        ExpressionAnalysis AnalysisFactory(FlowState state)
        {
            return new ExpressionAnalysis(_worklist, new LocalSymbolProvider(_compilation.GlobalSemantics, state.FlowContext));
        }

        internal void DiagnoseMethods()
        {
            this.WalkMethods(DiagnoseRoutine, allowParallel: true);
        }

        private void DiagnoseRoutine(SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(routine);

            DiagnosingVisitor.Analyse(_diagnostics, routine);
        }

        internal void DiagnoseFiles()
        {
            var files = _compilation.SourceSymbolCollection.GetFiles();

            if (ConcurrentBuild)
            {
                Parallel.ForEach(files, DiagnoseFile);
            }
            else
            {
                files.ForEach(DiagnoseFile);
            }
        }

        private void DiagnoseFile(SourceFileSymbol file)
        {
            file.GetDiagnostics(_diagnostics);
        }

        private void DiagnoseTypes()
        {
            this.WalkTypes(DiagnoseType, allowParallel: true);
        }

        private void DiagnoseType(SourceTypeSymbol type)
        {
            type.GetDiagnostics(_diagnostics);
        }

        internal void EmitMethodBodies()
        {
            Debug.Assert(_moduleBuilder != null);

            // source routines
            this.WalkMethods(this.EmitMethodBody, allowParallel: false); // TODO: in parallel
        }

        internal void EmitSynthesized()
        {
            // TODO: Visit every symbol with Synthesize() method and call it instead of followin

            // ghost stubs, overrides
            this.WalkMethods(f => f.SynthesizeStubs(_moduleBuilder, _diagnostics));
            this.WalkTypes(t => t.FinalizeMethodTable(_moduleBuilder, _diagnostics));

            // __statics.Init, .phpnew, .ctor
            WalkTypes(t => t.SynthesizeInit(_moduleBuilder, _diagnostics));

            // realize .cctor if any
            _moduleBuilder.RealizeStaticCtors();
        }

        /// <summary>
        /// Generates analyzed method.
        /// </summary>
        void EmitMethodBody(SourceRoutineSymbol routine)
        {
            Contract.ThrowIfNull(routine);

            if (routine.ControlFlowGraph != null)   // non-abstract method
            {
                Debug.Assert(routine.ControlFlowGraph.Start.FlowState != null);

                var body = MethodGenerator.GenerateMethodBody(_moduleBuilder, routine, 0, null, _diagnostics, _emittingPdb);
                _moduleBuilder.SetMethodBody(routine, body);
            }
        }

        void CompileEntryPoint()
        {
            if (_compilation.Options.OutputKind.IsApplication() && _moduleBuilder != null)
            {
                var entryPoint = _compilation.GetEntryPoint(_cancellationToken);
                if (entryPoint != null && !(entryPoint is ErrorMethodSymbol))
                {
                    // wrap call to entryPoint within real <Script>.EntryPointSymbol
                    _moduleBuilder.CreateEntryPoint((MethodSymbol)entryPoint, _diagnostics);

                    //
                    Debug.Assert(_moduleBuilder.ScriptType.EntryPointSymbol != null);
                    _moduleBuilder.SetPEEntryPoint(_moduleBuilder.ScriptType.EntryPointSymbol, _diagnostics);
                }
            }
        }

        void CompileReflectionEnumerators()
        {
            Debug.Assert(_moduleBuilder != null);

            _moduleBuilder.CreateEnumerateReferencedFunctions(_diagnostics);
            _moduleBuilder.CreateBuiltinTypes(_diagnostics);
            _moduleBuilder.CreateEnumerateScriptsSymbol(_diagnostics);
            _moduleBuilder.CreateEnumerateConstantsSymbol(_diagnostics);
        }

        public static IEnumerable<Diagnostic> BindAndAnalyze(PhpCompilation compilation)
        {
            var manager = compilation.GetBoundReferenceManager();   // ensure the references are resolved! (binds ReferenceManager)

            var diagnostics = new DiagnosticBag();
            var compiler = new SourceCompiler(compilation, null, true, diagnostics, CancellationToken.None);

            // 1. Bind Syntax & Symbols to Operations (CFG)
            //   a. construct CFG, bind AST to Operation
            //   b. declare table of local variables
            compiler.WalkMethods(compiler.EnqueueRoutine, allowParallel: true);
            compiler.WalkTypes(compiler.EnqueueFieldsInitializer, allowParallel: true);

            // 2. Analyze Operations
            //   a. type analysis (converge type - mask), resolve symbols
            //   b. lower semantics, update bound tree, repeat
            //   c. collect diagnostics
            compiler.AnalyzeMethods();
            compiler.DiagnoseMethods();
            compiler.DiagnoseTypes();
            compiler.DiagnoseFiles();

            //
            return diagnostics.AsEnumerable();
        }

        public static void CompileSources(
            PhpCompilation compilation,
            PEModuleBuilder moduleBuilder,
            bool emittingPdb,
            bool hasDeclarationErrors,
            DiagnosticBag diagnostics,
            CancellationToken cancellationToken)
        {
            Debug.Assert(moduleBuilder != null);

            // ensure flow analysis and collect diagnostics
            var declarationDiagnostics = compilation.GetDeclarationDiagnostics(cancellationToken);
            diagnostics.AddRange(declarationDiagnostics);

            // cancel the operation if there are errors
            if (hasDeclarationErrors |= declarationDiagnostics.HasAnyErrors() || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            //
            var compiler = new SourceCompiler(compilation, moduleBuilder, emittingPdb, diagnostics, cancellationToken);

            // Emit method bodies
            //   a. declared routines
            //   b. synthesized symbols
            compiler.EmitMethodBodies();
            compiler.EmitSynthesized();
            compiler.CompileReflectionEnumerators();

            // Entry Point (.exe)
            compiler.CompileEntryPoint();
        }
    }
}
