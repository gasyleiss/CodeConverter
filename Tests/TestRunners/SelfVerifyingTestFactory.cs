﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CodeConverter.Tests.Compilation;
using CodeConverter.Tests.CSharp;
using ICSharpCode.CodeConverter;
using ICSharpCode.CodeConverter.CSharp;
using ICSharpCode.CodeConverter.Shared;
using ICSharpCode.CodeConverter.Util;
using Xunit;
using Xunit.Sdk;

namespace CodeConverter.Tests.TestRunners
{
    internal class SelfVerifyingTestFactory
    {
        /// <summary>
        /// Returns facts which when executed, ensure the source Fact succeeds, then convert it, and ensure the target Fact succeeds too.
        /// </summary>
        public static IEnumerable<NamedFact> GetSelfVerifyingFacts<TSourceCompiler, TTargetCompiler, TLanguageConversion>(string testFilepath) 
            where TSourceCompiler : ICompiler, new() where TTargetCompiler : ICompiler, new() where TLanguageConversion : ILanguageConversion, new()
        {
            var sourceFileText = File.ReadAllText(testFilepath);
            var compiledSource = new TSourceCompiler().AssemblyFromCode(sourceFileText, AdditionalReferences);
            var runnableTestsInSource = XUnitFactDiscoverer.GetNamedFacts(compiledSource).ToList();
            Assert.NotEmpty(runnableTestsInSource);

            return GetSelfVerifyingFacts<TTargetCompiler, TLanguageConversion>(sourceFileText, runnableTestsInSource);
        }

        private static IEnumerable<NamedFact> GetSelfVerifyingFacts<TTargetCompiler, TLanguageConversion>(string sourceFileText,
                List<NamedFact> runnableTestsInSource) where TTargetCompiler : ICompiler, new()
            where TLanguageConversion : ILanguageConversion, new()
        {
            var conversionResult = ProjectConversion.ConvertText<TLanguageConversion>(sourceFileText, DefaultReferences.NetStandard2);

            // Avoid confusing test runner on error, but also avoid calculating multiple times
            var runnableTestsInTarget = new Lazy<Dictionary<string, NamedFact>>(() => GetConvertedNamedFacts<TTargetCompiler>(runnableTestsInSource,
                conversionResult));

            return runnableTestsInSource.Select(sourceFact =>
                new NamedFact(sourceFact.Name, () =>
                {
                    try
                    {
                        sourceFact.Execute();
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw new XunitException(
                            $"Source test failed, ensure the source is correct for \"{sourceFact.Name}\": {(ex.InnerException ?? ex)}");
                    }

                    try
                    {
                        runnableTestsInTarget.Value[sourceFact.Name].Execute();
                    }
                    catch (TargetInvocationException ex)
                    {
                        throw new XunitException(
                            $"Converted test failed, the conversion is incorrect for \"{sourceFact.Name}\": {(ex.InnerException ?? ex)}\r\nConverted Code: {conversionResult.ConvertedCode ?? conversionResult.GetExceptionsAsString()}");
                    }
                })
            );
        }

        private static Dictionary<string, NamedFact> GetConvertedNamedFacts<TTargetCompiler>(List<NamedFact> runnableTestsInSource, ConversionResult convertedText)
            where TTargetCompiler : ICompiler, new()
        {
            var compiledTarget = new TTargetCompiler().AssemblyFromCode(convertedText.ConvertedCode, AdditionalReferences);
            var runnableTestsInTarget = XUnitFactDiscoverer.GetNamedFacts(compiledTarget).ToDictionary(f => f.Name);

            Assert.Equal(runnableTestsInSource.Select(f => f.Name), runnableTestsInTarget.Keys);
            return runnableTestsInTarget;
        }

        private static Assembly[] AdditionalReferences =>
            new[] {typeof(Assert).Assembly, typeof(FactAttribute).Assembly};
    }
}