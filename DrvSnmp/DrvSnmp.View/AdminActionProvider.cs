// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Actions;
using Scada.Comm.Devices;
using Scada.Data.Const;
using Scada.Data.Models;
using Scada.Log;

namespace Scada.Comm.Drivers.DrvSnmp.View
{
    /// <summary>
    /// Provides web administrator actions for the SNMP driver.
    /// <para>Предоставляет действия веб-администратора для драйвера SNMP.</para>
    /// </summary>
    public sealed class AdminActionProvider
    {
        /// <summary>
        /// Returns channel prototypes generated from variable groups.
        /// </summary>
        [AdminAction(AdminActionIds.GetTags)]
        [AdminActionArgs(typeof(GetTagsArgs))]
        public Task<AdminActionResult> GetTags(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                GetTagsArgs args = context.GetArgs<GetTagsArgs>();
                int eventMask = new EventMask { Enabled = true, StatusChange = true }.Value;
                List<SnmpTagRow> rows = [];

                for (int groupIndex = 0; groupIndex < args.VarGroups.Count; groupIndex++)
                {
                    VarGroupRow group = args.VarGroups[groupIndex];
                    List<VariableRow> variables = groupIndex < args.VarGroupVariables.Count &&
                        args.VarGroupVariables[groupIndex].TryGetValue("variables", out List<VariableRow> groupVariables)
                            ? groupVariables
                            : [];

                    foreach (VariableRow variable in variables)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        TagDataType dataType = ParseDataType(variable.DataType);
                        int dataTypeID = (int)dataType;
                        int? dataLen = variable.DataLen > 0
                            ? DeviceTag.CalcDataLength(variable.DataLen, dataType)
                            : null;

                        rows.Add(new SnmpTagRow
                        {
                            Active = group.Active,
                            GroupName = group.Name,
                            Name = variable.Name,
                            TagCode = variable.TagCode,
                            OID = variable.OID,
                            DataTypeID = dataTypeID > 0 ? dataTypeID : null,
                            DataLen = dataLen,
                            CnlTypeID = CnlTypeID.Input,
                            EventMask = eventMask,
                            FormatCode = DataTypeID.IsString(dataTypeID) ? FormatCode.String : ""
                        });
                    }
                }

                return Task.FromResult(AdminActionResult.Table(new
                {
                    columns = new[]
                    {
                        "active",
                        "groupName",
                        "name",
                        "tagCode",
                        "oid",
                        "dataTypeID",
                        "dataLen",
                        "cnlTypeID",
                        "eventMask",
                        "formatCode"
                    },
                    rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(
                    ex.BuildErrorMessage("SNMP tag generation failed")));
            }
        }

        private static TagDataType ParseDataType(string dataType)
        {
            return Enum.TryParse(dataType, true, out TagDataType parsedDataType)
                ? parsedDataType
                : TagDataType.Double;
        }

        private sealed class GetTagsArgs
        {
            public List<VarGroupRow> VarGroups { get; set; } = [];
            public List<Dictionary<string, List<VariableRow>>> VarGroupVariables { get; set; } = [];
        }

        private sealed class VarGroupRow
        {
            public bool Active { get; set; } = true;
            public string Name { get; set; } = "";
        }

        private sealed class VariableRow
        {
            public string Name { get; set; } = "";
            public string TagCode { get; set; } = "";
            public string OID { get; set; } = "";
            public string DataType { get; set; } = "Double";
            public int DataLen { get; set; }
        }

        private sealed class SnmpTagRow
        {
            public bool Active { get; set; }
            public string GroupName { get; set; } = "";
            public string Name { get; set; } = "";
            public string TagCode { get; set; } = "";
            public string OID { get; set; } = "";
            public int? DataTypeID { get; set; }
            public int? DataLen { get; set; }
            public int CnlTypeID { get; set; }
            public int EventMask { get; set; }
            public string FormatCode { get; set; } = "";
        }
    }
}
