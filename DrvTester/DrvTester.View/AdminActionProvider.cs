// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Actions;
using Scada.Log;

namespace Scada.Comm.Drivers.DrvTester.View
{
    /// <summary>
    /// Provides web administrator actions for the communication channel tester driver.
    /// <para>Предоставляет действия веб-администратора для драйвера тестирования каналов связи.</para>
    /// </summary>
    public sealed class AdminActionProvider
    {
        /// <summary>
        /// Returns the tester commands supported by the device logic.
        /// </summary>
        [AdminAction(AdminActionIds.GetCommands)]
        public Task<AdminActionResult> GetCommands(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                TesterCommandRow[] rows =
                [
                    new()
                    {
                        CommandNumber = 1,
                        CommandCode = "SendStr",
                        Name = "Send string",
                        ValueType = "String"
                    },
                    new()
                    {
                        CommandNumber = 2,
                        CommandCode = "SendBin",
                        Name = "Send binary data",
                        ValueType = "Binary"
                    }
                ];

                return Task.FromResult(AdminActionResult.Table(new
                {
                    columns = new[] { "commandNumber", "commandCode", "name", "valueType" },
                    rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(
                    ex.BuildErrorMessage("Tester command generation failed")));
            }
        }

        private sealed class TesterCommandRow
        {
            public int CommandNumber { get; set; }
            public string CommandCode { get; set; } = "";
            public string Name { get; set; } = "";
            public string ValueType { get; set; } = "";
        }
    }
}
