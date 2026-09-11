// Copyright (c) Rapid Software LLC. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Opc.Ua;
using Opc.Ua.Client;
using Scada.Admin.Actions;
using Scada.Comm.Drivers.DrvOpcUa.Config;
using Scada.Log;

namespace Scada.Comm.Drivers.DrvOpcUa.View
{
    /// <summary>
    /// Provides web administrator actions for the OPC UA driver.
    /// <para>Предоставляет действия веб-администратора для драйвера OPC UA.</para>
    /// </summary>
    public sealed class AdminActionProvider
    {
        /// <summary>
        /// Tests connection to an OPC UA server.
        /// </summary>
        [AdminAction(AdminActionIds.TestConnection)]
        [AdminActionArgs(typeof(BrowseTagsArgs))]
        public async Task<AdminActionResult> TestConnection(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                using OpcActionSession actionSession = await CreateSessionAsync(context, cancellationToken);
                return AdminActionResult.Ok("OPC UA connection established successfully.");
            }
            catch (Exception ex)
            {
                return AdminActionResult.Error(ex.BuildErrorMessage("OPC UA connection failed"));
            }
        }

        /// <summary>
        /// Browses OPC UA tags.
        /// </summary>
        [AdminAction(AdminActionIds.BrowseTags)]
        [AdminActionArgs(typeof(BrowseTagsArgs))]
        public async Task<AdminActionResult> BrowseTags(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            try
            {
                BrowseTagsArgs args = context.GetArgs<BrowseTagsArgs>();

                using OpcActionSession actionSession = await CreateSessionAsync(context, cancellationToken);
                NodeId nodeId = string.IsNullOrWhiteSpace(args.NodeId)
                    ? ObjectIds.ObjectsFolder
                    : NodeId.Parse(args.NodeId);

                cancellationToken.ThrowIfCancellationRequested();

                Browser browser = new(actionSession.Session)
                {
                    BrowseDirection = BrowseDirection.Forward,
                    NodeClassMask = (int)NodeClass.Variable | (int)NodeClass.Object | (int)NodeClass.Method,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences
                };

                ReferenceDescriptionCollection browseResults = browser.Browse(nodeId);
                List<AdminActionTreeNode> nodes = [];

                foreach (ReferenceDescription rd in browseResults)
                {
                    NodeId childNodeId = ExpandedNodeId.ToNodeId(rd.NodeId, actionSession.Session.NamespaceUris);
                    AdminActionTreeNode node = new()
                    {
                        Id = childNodeId?.ToString() ?? "",
                        Text = rd.DisplayName.Text,
                        Kind = rd.NodeClass.ToString(),
                        HasChildren = rd.NodeClass is NodeClass.Object or NodeClass.Variable
                    };

                    node.Properties["browseName"] = rd.BrowseName.ToString();
                    node.Properties["nodeClass"] = rd.NodeClass.ToString();
                    nodes.Add(node);
                }

                return AdminActionResult.Tree(nodes);
            }
            catch (Exception ex)
            {
                return AdminActionResult.Error(ex.BuildErrorMessage("OPC UA browse failed"));
            }
        }

        /// <summary>
        /// Adds the selected OPC UA node to a subscription.
        /// </summary>
        [AdminAction(AdminActionIds.SetTags)]
        [AdminActionArgs(typeof(SetTagsArgs))]
        public async Task<AdminActionResult> SetTags(AdminActionContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetTagsArgs args = context.GetArgs<SetTagsArgs>();

            if (string.IsNullOrWhiteSpace(args.NodeId))
            {
                return AdminActionResult.Error("OPC UA node ID is not selected.");
            }

            if (args.Subscriptions.Count == 0)
            {
                args.Subscriptions.Add(new SubscriptionRow
                {
                    Active = true,
                    DisplayName = "Subscription 1",
                    PublishingInterval = 1000
                });
            }

            while (args.SubscriptionItems.Count < args.Subscriptions.Count)
            {
                args.SubscriptionItems.Add([]);
            }

            Dictionary<string, List<ItemRow>> firstSubscriptionItems = args.SubscriptionItems[0];

            if (!firstSubscriptionItems.TryGetValue("items", out List<ItemRow> items))
            {
                items = [];
                firstSubscriptionItems["items"] = items;
            }

            if (items.Any(item => string.Equals(item.NodeId, args.NodeId, StringComparison.OrdinalIgnoreCase)))
            {
                return AdminActionResult.FormPatch(
                    CreateSubscriptionsPatch(args.Subscriptions, args.SubscriptionItems),
                    "OPC UA node already exists in the subscription.");
            }

            string dataType = "";
            bool isArray = false;
            string warning = "";

            try
            {
                using OpcActionSession actionSession = await CreateSessionAsync(context, cancellationToken);
                NodeId nodeId = NodeId.Parse(args.NodeId);

                if (!TryGetDataType(actionSession.Session, nodeId, out dataType, out isArray, out warning))
                {
                    warning = string.IsNullOrWhiteSpace(warning)
                        ? "OPC UA data type was not read."
                        : warning;
                }
            }
            catch (Exception ex)
            {
                warning = ex.BuildErrorMessage("OPC UA data type was not read");
            }

            items.Add(new ItemRow
            {
                Active = true,
                NodeId = args.NodeId,
                DisplayName = BuildDisplayName(args.NodeId),
                TagCode = BuildTagCode(args.NodeId),
                DataType = dataType,
                IsArray = isArray,
                DataLen = 0
            });

            string message = string.IsNullOrWhiteSpace(warning)
                ? "OPC UA node added to the subscription."
                : "OPC UA node added to the subscription. " + warning;

            return AdminActionResult.FormPatch(
                CreateSubscriptionsPatch(args.Subscriptions, args.SubscriptionItems),
                message);
        }

        private static async Task<OpcActionSession> CreateSessionAsync(
            AdminActionContext context, CancellationToken cancellationToken)
        {
            BrowseTagsArgs args = context.GetArgs<BrowseTagsArgs>();

            if (string.IsNullOrWhiteSpace(args.ServerUrl))
            {
                args.ServerUrl = !string.IsNullOrWhiteSpace(args.EndpointUrl)
                    ? args.EndpointUrl
                    : context.GetString("endpointUrl");
            }

            if (string.IsNullOrWhiteSpace(args.ServerUrl))
            {
                throw new ScadaException("OPC UA server URL is not specified.");
            }

            OpcConnectionOptions connectionOptions = new()
            {
                ServerUrl = args.ServerUrl,
                Username = args.Username,
                Password = args.Password,
                AuthenticationMode = string.IsNullOrWhiteSpace(args.Username)
                    ? AuthenticationMode.Anonymous
                    : AuthenticationMode.Username,
                SecurityMode = ParseEnum(args.SecurityMode, MessageSecurityMode.None),
                SecurityPolicy = ParseEnum(args.SecurityPolicy, SecurityPolicy.None)
            };

            AppDirs appDirs = WebActionAppDirs.Create(context);
            Directory.CreateDirectory(appDirs.ConfigDir);

            OpcClientHelperView helper = new(connectionOptions, LogStub.Instance, appDirs)
            {
                AutoAccept = args.AutoAccept
            };

            cancellationToken.ThrowIfCancellationRequested();
            await helper.ConnectAsync();
            cancellationToken.ThrowIfCancellationRequested();

            return new OpcActionSession(helper.OpcSession);
        }

        private static T ParseEnum<T>(string value, T defaultValue) where T : struct
        {
            return string.IsNullOrWhiteSpace(value) || !Enum.TryParse(value, true, out T parsedValue)
                ? defaultValue
                : parsedValue;
        }

        private static bool TryGetDataType(
            ISession session,
            NodeId nodeId,
            out string dataTypeName,
            out bool isArray,
            out string warning)
        {
            dataTypeName = "";
            isArray = false;
            warning = "";

            try
            {
                ReadValueIdCollection nodesToRead =
                [
                    new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value
                    }
                ];

                session.Read(null, 0, TimestampsToReturn.Neither, nodesToRead,
                    out DataValueCollection results, out DiagnosticInfoCollection diagnosticInfos);
                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                DataValue nodeValue = results[0];

                if (StatusCode.IsNotGood(nodeValue.StatusCode) || nodeValue.Value == null)
                {
                    warning = "OPC UA node value is not available.";
                    return false;
                }

                Type dataType = nodeValue.Value.GetType();
                Type elemType = dataType.IsArray ? dataType.GetElementType() ?? dataType : dataType;
                dataTypeName = elemType.FullName ?? elemType.Name;
                isArray = dataType.IsArray && elemType != typeof(string);
                return true;
            }
            catch (Exception ex)
            {
                warning = ex.BuildErrorMessage("OPC UA data type read failed");
                return false;
            }
        }

        private static object CreateSubscriptionsPatch(
            List<SubscriptionRow> subscriptions,
            List<Dictionary<string, List<ItemRow>>> subscriptionItems)
        {
            return new
            {
                listValues = new Dictionary<string, object>
                {
                    ["subscriptions"] = subscriptions
                },
                nestedGridValues = new Dictionary<string, object>
                {
                    ["subscriptions"] = subscriptionItems
                }
            };
        }

        private static string BuildDisplayName(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
                return "";

            int separatorIndex = nodeId.LastIndexOfAny(['/', '.', ':', ';', '=']);
            string displayName = separatorIndex >= 0 && separatorIndex + 1 < nodeId.Length
                ? nodeId[(separatorIndex + 1)..]
                : nodeId;

            return string.IsNullOrWhiteSpace(displayName) ? nodeId : displayName;
        }

        private static string BuildTagCode(string nodeId)
        {
            string displayName = BuildDisplayName(nodeId);
            string tagCode = new(displayName
                .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
                .ToArray());

            tagCode = tagCode.Trim('_');
            return string.IsNullOrWhiteSpace(tagCode) ? "OpcUaTag" : tagCode;
        }

        private sealed class BrowseTagsArgs
        {
            public string ServerUrl { get; set; } = "";
            public string EndpointUrl { get; set; } = "";
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string SecurityMode { get; set; } = "";
            public string SecurityPolicy { get; set; } = "";
            public string NodeId { get; set; } = "";
            public bool AutoAccept { get; set; } = true;
        }

        private sealed class SetTagsArgs
        {
            public string ServerUrl { get; set; } = "";
            public string EndpointUrl { get; set; } = "";
            public string Username { get; set; } = "";
            public string Password { get; set; } = "";
            public string SecurityMode { get; set; } = "";
            public string SecurityPolicy { get; set; } = "";
            public string NodeId { get; set; } = "";
            public bool AutoAccept { get; set; } = true;
            public List<SubscriptionRow> Subscriptions { get; set; } = [];
            public List<Dictionary<string, List<ItemRow>>> SubscriptionItems { get; set; } = [];
        }

        private sealed class SubscriptionRow
        {
            public bool Active { get; set; } = true;
            public string DisplayName { get; set; } = "";
            public int PublishingInterval { get; set; } = 1000;
        }

        private sealed class ItemRow
        {
            public bool Active { get; set; } = true;
            public string NodeId { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string TagCode { get; set; } = "";
            public string DataType { get; set; } = "";
            public bool IsArray { get; set; }
            public int DataLen { get; set; }
        }

        private sealed class WebActionAppDirs : AppDirs
        {
            public static WebActionAppDirs Create(AdminActionContext context)
            {
                string baseDir = string.IsNullOrWhiteSpace(context.ProjectDir)
                    ? AppContext.BaseDirectory
                    : context.ProjectDir;

                WebActionAppDirs appDirs = new();
                appDirs.Init(baseDir);

                if (!string.IsNullOrWhiteSpace(context.ConfigDir))
                {
                    appDirs.ConfigDir = ScadaUtils.NormalDir(context.ConfigDir);
                }

                return appDirs;
            }
        }

        private sealed class OpcActionSession : IDisposable
        {
            public OpcActionSession(ISession session)
            {
                Session = session ?? throw new ArgumentNullException(nameof(session));
            }

            public ISession Session { get; }

            public void Dispose()
            {
                Session.Close();
                Session.Dispose();
            }
        }
    }
}
