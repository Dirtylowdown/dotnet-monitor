﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.Monitoring.Tool.FunctionalTests.Fixtures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Xunit.Abstractions;
using Xunit;
using System.Threading.Tasks;
using Microsoft.Diagnostics.Monitoring.Tool.FunctionalTests.Runners;
using Microsoft.Diagnostics.Monitoring.TestCommon;
using Microsoft.Diagnostics.Monitoring.TestCommon.Runners;
using Microsoft.Diagnostics.Monitoring.Tool.FunctionalTests.HttpApi;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Text.Json;

namespace Microsoft.Diagnostics.Monitoring.Tool.FunctionalTests
{
    [TargetFrameworkMonikerTrait(TargetFrameworkMonikerExtensions.CurrentTargetFrameworkMoniker)]
    [Collection(DefaultCollectionFixture.Name)]
    public class StackTests
    {
#if NET6_0_OR_GREATER

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ITestOutputHelper _outputHelper;

        private const string ExpectedModule = @"Microsoft.Diagnostics.Monitoring.UnitTestApp.dll";
        private const string ExpectedClass = @"Microsoft.Diagnostics.Monitoring.UnitTestApp.Scenarios.StacksWorker+StacksWorkerNested`1[System.Int32]";
        private const string ExpectedFunction = @"DoWork[System.Int64]";
        private const string ExpectedCallbackFunction = @"Callback";
        private const string NativeFrame = "[NativeFrame]";

        public StackTests(ITestOutputHelper outputHelper, ServiceProviderFixture serviceProviderFixture)
        {
            _httpClientFactory = serviceProviderFixture.ServiceProvider.GetService<IHttpClientFactory>();
            _outputHelper = outputHelper;
        }

        [Fact]
        public Task TestPlainTextStacks()
        {
            if (SkipIfWindowX86())
            {
                return Task.CompletedTask;
            }

            return ScenarioRunner.SingleTarget(
                _outputHelper,
                _httpClientFactory,
                WebApi.DiagnosticPortConnectionMode.Listen,
                TestAppScenarios.Stacks.Name,
                appValidate: async (runner, client) =>
                {
                    int processId = await runner.ProcessIdTask;

                    using ResponseStreamHolder holder = await client.CaptureStacksAsync(processId, plainText: true);
                    Assert.NotNull(holder);

                    using StreamReader reader = new StreamReader(holder.Stream);
                    string line = null;

                    string[] expectedFrames =
                    {
                        FormatFrame(ExpectedModule, ExpectedClass, ExpectedCallbackFunction),
                        NativeFrame,
                        FormatFrame(ExpectedModule, ExpectedClass, ExpectedFunction),
                    };

                    var actualFrames = new List<string>();

                    while ((line = reader.ReadLine()) != null)
                    {
                        line = line.TrimStart();
                        if (actualFrames.Count == expectedFrames.Length)
                        {
                            break;
                        }
                        if ((line == expectedFrames.First()) || (actualFrames.Count > 0))
                        {
                            actualFrames.Add(line);
                        }
                    }

                    Assert.Equal(expectedFrames, actualFrames);

                    await runner.SendCommandAsync(TestAppScenarios.Stacks.Commands.Continue);
                });
        }

        [Fact]
        public Task TestJsonStacks()
        {
            if (SkipIfWindowX86())
            {
                return Task.CompletedTask;
            }

            return ScenarioRunner.SingleTarget(
                _outputHelper,
                _httpClientFactory,
                WebApi.DiagnosticPortConnectionMode.Listen,
                TestAppScenarios.Stacks.Name,
                appValidate: async (runner, client) =>
                {
                    int processId = await runner.ProcessIdTask;

                    using ResponseStreamHolder holder = await client.CaptureStacksAsync(processId, plainText: false);
                    Assert.NotNull(holder);

                    WebApi.Models.CallStackResult result = await JsonSerializer.DeserializeAsync<WebApi.Models.CallStackResult>(holder.Stream);

                    var actualFrames = new List<WebApi.Models.CallStackFrame>();
                    WebApi.Models.CallStackFrame[] expectedFrames = new WebApi.Models.CallStackFrame[]
                    {
                        new WebApi.Models.CallStackFrame
                        {
                            ModuleName = ExpectedModule,
                            ClassName = ExpectedClass,
                            MethodName = ExpectedCallbackFunction
                        },
                        new WebApi.Models.CallStackFrame
                        {
                            ModuleName = NativeFrame,
                            ClassName = NativeFrame,
                            MethodName = NativeFrame
                        },
                        new WebApi.Models.CallStackFrame
                        {
                            ModuleName = ExpectedModule,
                            ClassName = ExpectedClass,
                            MethodName = ExpectedFunction
                        }
                    };

                    foreach (WebApi.Models.CallStack stack in result.Stacks)
                    {
                        actualFrames.Clear();
                        foreach(var frame in stack.Frames)
                        {
                            if (actualFrames.Count == expectedFrames.Length)
                            {
                                break;
                            }
                            if (AreFramesEqual(expectedFrames.First(), frame) || actualFrames.Count > 0)
                            {
                                actualFrames.Add(frame);
                            }
                        }
                    }

                    Assert.Equal(expectedFrames.Length, actualFrames.Count);

                    for (int i = 0; i < expectedFrames.Length; i++)
                    {
                        Assert.True(AreFramesEqual(expectedFrames[i], actualFrames[i]));
                    }

                    await runner.SendCommandAsync(TestAppScenarios.Stacks.Commands.Continue);
                });
        }

        [Fact]
        public Task TestRepeatStackCalls()
        {
            if (SkipIfWindowX86())
            {
                return Task.CompletedTask;
            }

            return ScenarioRunner.SingleTarget(
                _outputHelper,
                _httpClientFactory,
                WebApi.DiagnosticPortConnectionMode.Listen,
                TestAppScenarios.Stacks.Name,
                appValidate: async (runner, client) =>
                {
                    int processId = await runner.ProcessIdTask;

                    using ResponseStreamHolder holder1 = await client.CaptureStacksAsync(processId, plainText: false);
                    Assert.NotNull(holder1);

                    WebApi.Models.CallStackResult result1 = await JsonSerializer.DeserializeAsync<WebApi.Models.CallStackResult>(holder1.Stream);

                    using ResponseStreamHolder holder2 = await client.CaptureStacksAsync(processId, plainText: false);
                    Assert.NotNull(holder2);

                    WebApi.Models.CallStackResult result2 = await JsonSerializer.DeserializeAsync<WebApi.Models.CallStackResult>(holder2.Stream);

                    Assert.NotEmpty(result1.Stacks);
                    Assert.NotEmpty(result2.Stacks);

                    Assert.NotEmpty(result1.Stacks.SelectMany(s => s.Frames));
                    Assert.NotEmpty(result2.Stacks.SelectMany(s => s.Frames));


                    await runner.SendCommandAsync(TestAppScenarios.Stacks.Commands.Continue);
                });
        }

        private bool SkipIfWindowX86()
        {
            if (ProfilerHelper.TargetRuntimeIdentifier == "win-x86")
            {
                _outputHelper.WriteLine("Skipping x86 architecture since x86 host is not used at this time.");
                return true;
            }
            return false;
        }

#endif

        private static string FormatFrame(string module, string @class, string function) =>
            FormattableString.Invariant($"{module}!{@class}.{function}");

        private static bool AreFramesEqual(WebApi.Models.CallStackFrame left, WebApi.Models.CallStackFrame right) =>
            (left.ModuleName == right.ModuleName) && (left.ClassName == right.ClassName) && (left.MethodName == right.MethodName);
    }
}
