﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.DotNet.UpgradeAssistant.Cli
{
    internal class NonInteractiveUserInput : IUserInput
    {
        private readonly TimeSpan _waitPeriod;

        public NonInteractiveUserInput(UpgradeOptions options)
        {
            _waitPeriod = TimeSpan.FromSeconds(options.NonInteractiveWait);
        }

        public Task<string?> AskUserAsync(string currentPath)
        {
            throw new NotImplementedException("User input cannot be selected in non-interactive mode");
        }

        public Task<T> ChooseAsync<T>(string message, IEnumerable<T> commands, CancellationToken token, UpgradeStep? currentStep = null)
            where T : UpgradeCommand
        {
            if (currentStep != null && currentStep.Id != "Microsoft.DotNet.UpgradeAssistant.Steps.ProjectFormat.SetTFMStep")
            {
                return Task.FromResult(commands.First(c => c.IsEnabled));
            }
            else
            {
                return Task.FromResult(commands.Last(c => c.IsEnabled));
            }
        }

        public async Task<bool> WaitToProceedAsync(CancellationToken token)
        {
            await Task.Delay(_waitPeriod, token);

            return true;
        }
    }
}
