﻿namespace VisualMutator.Model
{
    #region

    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using CSharpSourceEmitter;
    using Decompilation;
    using Decompilation.PeToText;
    using Exceptions;
    using log4net;
    using Microsoft.Cci;
    using Microsoft.Cci.ILToCodeModel;
    using Microsoft.Cci.MutableCodeModel;
    using StoringMutants;
    using VisualMutator.Infrastructure;
    using Assembly = Microsoft.Cci.MutableCodeModel.Assembly;
    using Module = Microsoft.Cci.MutableCodeModel.Module;
    using SourceEmitter = CSharpSourceEmitter.SourceEmitter;

    #endregion

    public interface ICciModuleSource : IModuleSource
    {
        IModuleInfo AppendFromFile(string filePath);
        MemoryStream WriteToStream(IModuleInfo module);
        void WriteToStream(IModuleInfo module, Stream stream);
        MetadataReaderHost Host { get; }
        List<ModuleInfo> ModulesInfo { get; }
        SourceEmitter GetSourceEmitter(CodeLanguage language, IModule assembly, SourceEmitterOutputString sourceEmitterOutput);
    }

    public class CciModuleSource : IDisposable, ICciModuleSource
    {
        private readonly MetadataReaderHost _host;
        private List<ModuleInfo> _moduleInfoList;
        private readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public List<IModuleInfo> Modules
        {
            get { return _moduleInfoList.Cast<IModuleInfo>().ToList(); }
        }
        public List<ModuleInfo> ModulesInfo
        {
            get
            {
                return _moduleInfoList;
            }
        }
        public CciModuleSource()
        {
            _host = new PeReader.DefaultHost();
            _moduleInfoList = new List<ModuleInfo>();
        }
        public CciModuleSource(MetadataReaderHost host, List<ModuleInfo> moduleInfoList)
        {
            _host = host;
            _moduleInfoList = moduleInfoList;
        }
        public CciModuleSource(ProjectFilesClone filesClone) : this()
        {
            foreach (var assembliesPath in filesClone.Assemblies)
            {
                var sss = new CodeDeepCopier(Host);
                var m = DecompileFile(assembliesPath.Path);
                var copied = sss.Copy(m.Module);
                m.Module = copied;
                _moduleInfoList.Add(m);
            }
        }
        public CciModuleSource(string path) : this()
        {
            var sss = new CodeDeepCopier(this.Host);
            var m = DecompileFile(path);
            var copied = sss.Copy(m.Module);
            m.Module = copied;
            _moduleInfoList.Add(m);
        }

        private CciModuleSource(MetadataReaderHost host, IModule module)
        {
            _host = host;
            _moduleInfoList = new List<ModuleInfo>();
            _moduleInfoList.Add(new ModuleInfo(module));
        }

        public MetadataReaderHost Host
        {
            get { return _host; }
        }

        public ModuleInfo Module { get { return (ModuleInfo) Modules.Single(); } }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        public void Dispose(bool disposing)
        {
            foreach (var moduleInfo in _moduleInfoList)
            {
                if (moduleInfo.PdbReader != null)
                {
                    moduleInfo.PdbReader.Dispose();
                }
            }
           
            _moduleInfoList.Clear();
            _host.Dispose();

        }

        ~CciModuleSource()
        {
            Dispose(false);
        }

    

        public SourceEmitter GetSourceEmitter(CodeLanguage lang, IModule module, SourceEmitterOutputString output)
        {
            var moduleInfo = _moduleInfoList.Single(m => m.Module.Name.UniqueKey == module.Name.UniqueKey);
            var reader = moduleInfo.PdbReader;
             return new VisualSourceEmitter(output, _host, reader, noIL: lang == CodeLanguage.CSharp, printCompilerGeneratedMembers: false);
        }

        public ModuleInfo DecompileFile(string filePath)
        {
            _log.Info("Decompiling file: " + filePath);
            IModule module = _host.LoadUnitFrom(filePath) as IModule;
            if (module == null || module == Dummy.Module || module == Dummy.Assembly)
            {
                throw new AssemblyReadException(filePath + " is not a PE file containing a CLR module or assembly.");
            }

            PdbReader pdbReader = null;
            string pdbFile = Path.ChangeExtension(module.Location, "pdbx");
            if (File.Exists(pdbFile))
            {
                Stream pdbStream = File.OpenRead(pdbFile);
                pdbReader = new PdbReader(pdbStream, _host);
                pdbStream.Close();
            }
            
            Module decompiledModule = Decompiler.GetCodeModelFromMetadataModel(_host, module, pdbReader);
            ISourceLocationProvider sourceLocationProvider = pdbReader;
            ILocalScopeProvider localScopeProvider = new Decompiler.LocalScopeProvider(pdbReader);
            _log.Info("Decompiling file finished: " + filePath);
            return new ModuleInfo(decompiledModule, filePath)
            {
                PdbReader = pdbReader,
                LocalScopeProvider = localScopeProvider,
                SourceLocationProvider = sourceLocationProvider,
            };
        }
      
        public IModuleInfo AppendFromFile(string filePath)
        {
            _log.Info("CommonCompilerInfra.AppendFromFile:" + filePath);
            ModuleInfo module = DecompileFile(filePath);
            _moduleInfoList.Add(module);
            return module;
        }


        public IModuleInfo FindModuleInfo(IModule module)
        {
            return _moduleInfoList.First(m => m.Module.Name.Value == module.Name.Value);
        }

        public MemoryStream WriteToStream(IModuleInfo moduleInfo)
        {
            var module = (ModuleInfo)moduleInfo;
            _log.Info("CommonCompilerInfra.WriteToFile:" + module.Name);
            MemoryStream stream = new MemoryStream();

            if (module.PdbReader == null)
            {
                PeWriter.WritePeToStream(module.Module, _host, stream);
                
            }
            else
            {
                throw new NotImplementedException();
//                using (var pdbWriter = new PdbWriter(Path.ChangeExtension(filePath, "pdb"), module.PdbReader))
//                {
//                    PeWriter.WritePeToStream(module.Module, _host, stream, module.SourceLocationProvider,
//                        module.LocalScopeProvider, pdbWriter);
//                }
            }
            stream.Position = 0;

            return stream;
        }

        public void WriteToStream(IModuleInfo module, Stream stream )
        {
            PeWriter.WritePeToStream(module.Module, _host, stream);
        }

        public void ReplaceWith(IModule newMod)
        {
         //   if(newMod)
            var s = _moduleInfoList.SingleOrDefault(m => m.Name == newMod.Name.Value);
            if (s != null)
            {
                s.Module = newMod;
                _moduleInfoList = _moduleInfoList.Where(m => m == s).ToList();
            }

        }
        public void ReplaceWith(List<IModule> modules)
        {
            foreach (var moduleInfo in _moduleInfoList)
            {
                moduleInfo.Module = modules.Single(m => m.Name.Value == moduleInfo.Name);
            }
        }
        public CciModuleSource CloneWith(IModule newMod)
        {
            var cci = new CciModuleSource(Host, _moduleInfoList);
            cci.ReplaceWith(newMod);
            return cci;
        }
        public CciModuleSource Copy()
        {
            //ModuleInfo moduleInfo = (ModuleInfo) Modules.Single();
            var host = new PeReader.DefaultHost();
            var c = new CodeDeepCopier(host);//, moduleInfo.SourceLocationProvider, moduleInfo.LocalScopeProvider);
            var newMod = c.Copy(Modules.Single().Module);
            return new CciModuleSource(host, newMod);
        }
        public CodeDeepCopier CreateCopier()
        {
            //ModuleInfo moduleInfo = (ModuleInfo) Modules.Single();
            return new CodeDeepCopier(Host);//, moduleInfo.SourceLocationProvider, moduleInfo.LocalScopeProvider);
            
        }
    }
}