// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Scada.Admin.Actions;
using Scada.Comm.Drivers.DrvModbus.Protocol;
using Scada.Log;

namespace Scada.Comm.Drivers.DrvModbus.View
{
    /// <summary>
    /// Provides web administrator actions for the Modbus driver.
    /// <para>Предоставляет действия веб-администратора для драйвера Modbus.</para>
    /// </summary>
    public sealed class AdminActionProvider
    {
        /// <summary>
        /// Builds a flat tag list from read commands and their registers.
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
                List<ModbusTagRow> rows = [];

                for (int commandIndex = 0; commandIndex < args.ReadCommands.Count; commandIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ReadCommandRow readCommand = args.ReadCommands[commandIndex];

                    if (!readCommand.Active)
                        continue;

                    List<RegisterRow> registers = commandIndex < args.ReadCommandRegisters.Count &&
                        args.ReadCommandRegisters[commandIndex].TryGetValue("registers", out List<RegisterRow> commandRegisters)
                            ? commandRegisters
                            : [];

                    int address = readCommand.Address;
                    string dataBlock = string.IsNullOrWhiteSpace(readCommand.DataBlock)
                        ? "HoldingRegisters"
                        : readCommand.DataBlock;
                    string blockCode = GetBlockCode(dataBlock);
                    string commandName = string.IsNullOrWhiteSpace(readCommand.Name)
                        ? $"Read command {commandIndex + 1}"
                        : readCommand.Name;

                    foreach (RegisterRow register in registers)
                    {
                        string elementType = string.IsNullOrWhiteSpace(register.ElementType)
                            ? "ushort"
                            : register.ElementType;
                        string tagCode = $"{blockCode}{address:D5}";

                        rows.Add(new ModbusTagRow
                        {
                            ReadCommand = commandName,
                            TagCode = tagCode,
                            Name = string.IsNullOrWhiteSpace(register.Name)
                                ? $"{GetBlockName(dataBlock)} {address}"
                                : register.Name,
                            DataBlock = dataBlock,
                            Address = address,
                            ElementType = elementType
                        });

                        address += GetElementQuantity(elementType);
                    }
                }

                return Task.FromResult(AdminActionResult.Table(new
                {
                    columns = new[] { "readCommand", "tagCode", "name", "dataBlock", "address", "elementType" },
                    rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(ex.BuildErrorMessage("Modbus tag generation failed")));
            }
        }

        /// <summary>
        /// Builds a flat command list from the current command table.
        /// </summary>
        [AdminAction(AdminActionIds.GetCommands)]
        [AdminActionArgs(typeof(GetCommandsArgs))]
        public Task<AdminActionResult> GetCommands(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                GetCommandsArgs args = context.GetArgs<GetCommandsArgs>();
                List<ModbusCommandRow> rows = [];

                foreach (CommandRow command in args.Commands)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    string dataBlock = string.IsNullOrWhiteSpace(command.DataBlock)
                        ? "Coils"
                        : command.DataBlock;
                    bool multiple = command.Multiple || command.ElementCount > 1;
                    byte functionCode = GetWriteFunctionCode(dataBlock, multiple, command.FunctionCode);

                    rows.Add(new ModbusCommandRow
                    {
                        CommandCode = command.CommandCode,
                        CommandNumber = command.CommandNumber,
                        Name = command.Name,
                        DataBlock = dataBlock,
                        Address = command.Address,
                        Multiple = multiple,
                        ElementType = command.ElementType,
                        ElementCount = Math.Max(command.ElementCount, 1),
                        FunctionCode = functionCode == 0 ? "" : $"0x{functionCode:X2}"
                    });
                }

                return Task.FromResult(AdminActionResult.Table(new
                {
                    columns = new[]
                    {
                        "commandCode",
                        "commandNumber",
                        "name",
                        "dataBlock",
                        "address",
                        "multiple",
                        "elementType",
                        "elementCount",
                        "functionCode"
                    },
                    rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(ex.BuildErrorMessage("Modbus command generation failed")));
            }
        }

        /// <summary>
        /// Creates sample register rows for checking nested grid editing in the web administrator.
        /// </summary>
        [AdminAction("GetSampleRegisters")]
        public Task<AdminActionResult> GetSampleRegisters(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                RegisterRow[] rows =
                [
                    new RegisterRow
                    {
                        TagCode = "HR00000",
                        Name = "Temperature",
                        ElementType = "float",
                        ByteOrder = "0123",
                        ReadOnly = true
                    },
                    new RegisterRow
                    {
                        TagCode = "HR00002",
                        Name = "Pressure",
                        ElementType = "ushort",
                        ReadOnly = true
                    },
                    new RegisterRow
                    {
                        TagCode = "HR00003",
                        Name = "Speed",
                        ElementType = "ushort"
                    }
                ];

                return Task.FromResult(AdminActionResult.List(rows));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(ex.BuildErrorMessage("Modbus sample register generation failed")));
            }
        }

        /// <summary>
        /// Shows registers of the selected read command passed by the web descriptor.
        /// </summary>
        [AdminAction("PreviewSelectedReadCommand")]
        [AdminActionArgs(typeof(PreviewSelectedReadCommandArgs))]
        public Task<AdminActionResult> PreviewSelectedReadCommand(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                PreviewSelectedReadCommandArgs args = context.GetArgs<PreviewSelectedReadCommandArgs>();
                ReadCommandRow command = args.ReadCommand ?? new ReadCommandRow();
                List<RegisterRow> registers = args.ReadCommandRegisters != null &&
                    args.ReadCommandRegisters.TryGetValue("registers", out List<RegisterRow> commandRegisters)
                        ? commandRegisters
                        : [];
                string commandName = string.IsNullOrWhiteSpace(command.Name)
                    ? "Selected command"
                    : command.Name;
                string dataBlock = string.IsNullOrWhiteSpace(command.DataBlock)
                    ? "HoldingRegisters"
                    : command.DataBlock;
                List<SelectedRegisterPreviewRow> rows = [];

                foreach (RegisterRow register in registers)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    rows.Add(new SelectedRegisterPreviewRow
                    {
                        ReadCommand = commandName,
                        DataBlock = dataBlock,
                        TagCode = register.TagCode,
                        Name = register.Name,
                        ElementType = register.ElementType,
                        ByteOrder = register.ByteOrder,
                        ReadOnly = register.ReadOnly
                    });
                }

                return Task.FromResult(AdminActionResult.Table(new
                {
                    columns = new[] { "readCommand", "dataBlock", "tagCode", "name", "elementType", "byteOrder", "readOnly" },
                    rows
                }));
            }
            catch (Exception ex)
            {
                return Task.FromResult(AdminActionResult.Error(ex.BuildErrorMessage("Modbus selected command preview failed")));
            }
        }

        private static string GetBlockCode(string registerType)
        {
            return NormalizeRegisterType(registerType) switch
            {
                "coil" => "C",
                "discrete" => "DI",
                "inputregister" => "IR",
                _ => "HR"
            };
        }

        private static int GetElementQuantity(string elementType)
        {
            return NormalizeRegisterType(elementType) switch
            {
                "int" or "uint" or "float" or "single" => 2,
                "long" or "ulong" or "double" => 4,
                _ => 1
            };
        }

        private static string GetBlockName(string registerType)
        {
            return NormalizeRegisterType(registerType) switch
            {
                "coil" => "Coil",
                "discrete" => "Discrete input",
                "inputregister" => "Input register",
                _ => "Holding register"
            };
        }

        private static string NormalizeRegisterType(string registerType)
        {
            return (registerType ?? "").Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant();
        }

        private static byte GetWriteFunctionCode(string dataBlock, bool multiple, int customFunctionCode)
        {
            return NormalizeRegisterType(dataBlock) switch
            {
                "coil" or "coils" => ModbusUtils.GetWriteFuncCode(DataBlock.Coils, multiple),
                "holdingregister" or "holdingregisters" => ModbusUtils.GetWriteFuncCode(DataBlock.HoldingRegisters, multiple),
                "custom" => customFunctionCode > 0 && customFunctionCode <= byte.MaxValue
                    ? (byte)customFunctionCode
                    : (byte)0,
                _ => 0
            };
        }

        private sealed class GetTagsArgs
        {
            public List<ReadCommandRow> ReadCommands { get; set; } = [];
            public List<Dictionary<string, List<RegisterRow>>> ReadCommandRegisters { get; set; } = [];
        }

        private sealed class GetCommandsArgs
        {
            public List<CommandRow> Commands { get; set; } = [];
        }

        private sealed class PreviewSelectedReadCommandArgs
        {
            public ReadCommandRow ReadCommand { get; set; } = new();
            public Dictionary<string, List<RegisterRow>> ReadCommandRegisters { get; set; } = [];
        }

        private sealed class RegisterRow
        {
            public string TagCode { get; set; } = "";
            public string Name { get; set; } = "";
            public string ElementType { get; set; } = "ushort";
            public string ByteOrder { get; set; } = "";
            public bool ReadOnly { get; set; }
            public bool IsBitMask { get; set; }
        }

        private sealed class ReadCommandRow
        {
            public bool Active { get; set; } = true;
            public string Name { get; set; } = "";
            public string DataBlock { get; set; } = "HoldingRegisters";
            public int Address { get; set; }
        }

        private sealed class ModbusTagRow
        {
            public string ReadCommand { get; init; } = "";
            public string TagCode { get; init; } = "";
            public string Name { get; init; } = "";
            public string DataBlock { get; init; } = "";
            public int Address { get; init; }
            public string ElementType { get; init; } = "";
        }

        private sealed class SelectedRegisterPreviewRow
        {
            public string ReadCommand { get; init; } = "";
            public string DataBlock { get; init; } = "";
            public string TagCode { get; init; } = "";
            public string Name { get; init; } = "";
            public string ElementType { get; init; } = "";
            public string ByteOrder { get; init; } = "";
            public bool ReadOnly { get; init; }
        }

        private sealed class CommandRow
        {
            public string CommandCode { get; set; } = "";
            public int CommandNumber { get; set; }
            public string Name { get; set; } = "";
            public string DataBlock { get; set; } = "Coils";
            public int Address { get; set; }
            public bool Multiple { get; set; }
            public string ElementType { get; set; } = "bool";
            public int ElementCount { get; set; } = 1;
            public int FunctionCode { get; set; }
        }

        private sealed class ModbusCommandRow
        {
            public string CommandCode { get; init; } = "";
            public int CommandNumber { get; init; }
            public string Name { get; init; } = "";
            public string DataBlock { get; init; } = "";
            public int Address { get; init; }
            public bool Multiple { get; init; }
            public string ElementType { get; init; } = "";
            public int ElementCount { get; init; }
            public string FunctionCode { get; init; } = "";
        }
    }
}
