﻿namespace VisualMutator.Model.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;

    using CommonUtilityInfrastructure;

    using Mono.Cecil;

    using VisualMutator.Controllers;
    using VisualMutator.Model.Mutations;
    using VisualMutator.Model.Mutations.Structure;
    using VisualMutator.Model.Tests.Services;
    using VisualMutator.Model.Tests.TestsTree;

    using Switch = CommonUtilityInfrastructure.Switch;

    public interface ITestsContainer
    {
       // TestSession LoadTests(StoredMutantInfo mutant);

        void RunTests(TestSession testSession);

 
        void UnloadTests();



        void RunTestsForMutant(MutationTestingSession session, TestEnvironmentInfo testEnvironmentInfo, Mutant mutant);

        TestEnvironmentInfo InitTestEnvironment(MutationTestingSession currentSession);

        void CleanupTestEnvironment(TestEnvironmentInfo testEnvironmentInfo);

        void CancelTestRun();
    }

    public class TestsContainer : ITestsContainer
    {
        private readonly IMutantsFileManager _mutantsFileManager;

        private readonly CommonServices _commonServices;

        private readonly IAssemblyVerifier _assemblyVerifier;

        private readonly IEnumerable<ITestService> _testServices;

      

        private StoredMutantInfo _currentMutant;

     

        public TestsContainer(
            NUnitTestService nunit, 
            MsTestService ms,
            IMutantsFileManager mutantsFileManager, 
            CommonServices commonServices,
            IAssemblyVerifier assemblyVerifier)
        {
            _mutantsFileManager = mutantsFileManager;
            _commonServices = commonServices;
            _assemblyVerifier = assemblyVerifier;
            _testServices = new List<ITestService>
            {
                nunit,ms
            };
        }

        public void VerifyAssemblies(List<string> assembliesPaths)
        {
            foreach (var assemblyPath in assembliesPaths)
            {
                _assemblyVerifier.Verify(assemblyPath);
            }
  
        }
        public TestEnvironmentInfo InitTestEnvironment(MutationTestingSession currentSession)
        {
            return _mutantsFileManager.InitTestEnvironment(currentSession);
        }


        public void CleanupTestEnvironment(TestEnvironmentInfo testEnvironmentInfo)
        {
            _mutantsFileManager.CleanupTestEnvironment(testEnvironmentInfo);
        }



        public void RunTestsForMutant(MutationTestingSession session, TestEnvironmentInfo testEnvironmentInfo, Mutant mutant)
        {
            var sw = new Stopwatch();
            sw.Start();

            mutant.State = MutantResultState.Tested;

            StoredMutantInfo storedMutantInfo= _mutantsFileManager.StoreMutant(testEnvironmentInfo, mutant);

            IDisposable timoutDisposable = null;
            try
            {
                if (session.Options.IsMutantVerificationEnabled)
                {
                    VerifyAssemblies(storedMutantInfo.AssembliesPaths);
                }
                LoadTests(storedMutantInfo, mutant.TestSession);

                timoutDisposable = Observable.Timer(TimeSpan.FromSeconds(session.MutationSessionChoices.TestingTimeoutSeconds))
                    .Subscribe(e => CancelTestRun());
      

                RunTests(mutant.TestSession);
                
                timoutDisposable.Dispose();

                UnloadTests();


                ResolveMutantState(mutant);

                mutant.TestSession.IsComplete = true;
            }
            catch (AssemblyVerificationException e)
            {

                mutant.TestSession.ErrorDescription = "Mutant assembly failed verification";
                mutant.TestSession.ErrorMessage = e.Message;
                mutant.TestSession.Exception = e;
                mutant.State = MutantResultState.Error;
               
            }
            catch (Exception e)
            {

                mutant.TestSession.ErrorDescription = "Error ocurred";
                mutant.TestSession.ErrorMessage = e.Message;
                mutant.TestSession.Exception = e;
                mutant.State = MutantResultState.Error;
            }
            finally
            {
                if (timoutDisposable != null)
                {
                    timoutDisposable.Dispose();
                }
                sw.Stop();
                mutant.TestSession.TestingTimeMiliseconds = sw.ElapsedMilliseconds;
                
                
            }
            
            
        }

        private void ResolveMutantState(Mutant mutant)
        {
            mutant.NumberOfFailedTests = mutant.TestSession.TestMap.Values
                          .Count(t => t.State.IsIn(TestNodeState.Failure, TestNodeState.Inconclusive));



            if (mutant.TestSession.TestMap.Values.Any(t => t.State == TestNodeState.Inconclusive))
            {
                
                mutant.KilledSubstate = MutantKilledSubstate.Inconclusive;
                mutant.State = MutantResultState.Killed;
            }
            else if (mutant.TestSession.TestMap.Values.Any(t => t.State == TestNodeState.Running))
            {
                
                //TODO: not accurate and can be wrong
                mutant.KilledSubstate = MutantKilledSubstate.TimedOut;
                mutant.State = MutantResultState.Killed;
            }
            else if (mutant.TestSession.TestMap.Values.Any(t => t.State == TestNodeState.Failure))
            {
              
                mutant.KilledSubstate = MutantKilledSubstate.Normal;
                mutant.State = MutantResultState.Killed;
            }
            else if (mutant.TestSession.TestMap.Values.All(t => t.State == TestNodeState.Success))
            {
                mutant.State = MutantResultState.Live;
            }
            else
            {
                throw new InvalidOperationException("Unknown state");
            }

        }

        public void CancelTestRun()
        {
            foreach (var service in _testServices)
            {
                service.Cancel();
            }
        }

        public void LoadTests(StoredMutantInfo mutant, TestSession testSession)
        {
            if (mutant == null)
            {
                throw new ArgumentNullException("mutant");
            }
            _currentMutant = mutant;
      

            IEnumerable<TestNodeClass> testClassses = _testServices
                .SelectMany(s => s.LoadTests(mutant.AssembliesPaths, testSession));

            testSession.TestClassses.AddRange(testClassses);

            List<TestNodeNamespace> testNamespaces = testSession.TestClassses
                .GroupBy(classNode => classNode.Namespace)
                .Select(group =>
                {
                    var ns = new TestNodeNamespace(testSession.TestsRootNode, group.Key);
                    foreach (var nodeClass in group)
                    {
                        nodeClass.Parent = ns;
                    }
       
                    ns.Children.AddRange(group);
                    return ns;

                }).ToList();


            testSession.TestsRootNode.Children.AddRange(testNamespaces);
            testSession.TestsRootNode.State = TestNodeState.Inactive;

          
        }



        public void RunTests(TestSession testSession)
        {
            testSession.TestsRootNode.State = TestNodeState.Running;

            foreach (var service in _testServices)
            {
                service.RunTests(testSession);
            }
        }


        public void UnloadTests()
        {
            if (_currentMutant != null)
            {
                foreach (ITestService testService in _testServices)
                {
                    testService.UnloadTests();
                }
                _currentMutant = null;
            }
            
        }

    }
}