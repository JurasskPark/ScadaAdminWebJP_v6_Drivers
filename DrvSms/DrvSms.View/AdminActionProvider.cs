// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Actions;
using Scada.Data.Const;
using Scada.Lang;
using Scada.Log;

namespace Scada.Comm.Drivers.DrvSms.View
{
    /// <summary>
    /// Provides web administrator actions for the SMS driver.
    /// <para>Предоставляет действия веб-администратора для SMS-драйвера.</para>
    /// </summary>
    public sealed class AdminActionProvider
    {
        /// <summary>
        /// Returns static channel prototypes of the SMS driver.
        /// </summary>
        [AdminAction(AdminActionIds.GetTags)]
        public Task<AdminActionResult> GetTags(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                List<SmsTagRow> rows =
                [
                    new()
                    {
                        Name = Locale.IsRussian ? "Сообщения" : "Messages",
                        TagCode = "Msg",
                        CnlTypeID = CnlTypeID.InputOutput,
                        FormatCode = FormatCode.N0
                    },
                    new()
                    {
                        Name = Locale.IsRussian ? "AT-комманды" : "AT commands",
                        TagCode = "AtCmd",
                        CnlTypeID = CnlTypeID.InputOutput,
                        FormatCode = FormatCode.N0
                    }
                ];

                return Task.FromResult(AdminActionResult.Table(new
                {
                    columns = new[] { "name", "tagCode", "cnlTypeID", "formatCode" },
                    rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(
                    ex.BuildErrorMessage("SMS tag generation failed")));
            }
        }

        private sealed class SmsTagRow
        {
            public string Name { get; set; } = "";
            public string TagCode { get; set; } = "";
            public int CnlTypeID { get; set; }
            public string FormatCode { get; set; } = "";
        }
    }
}
