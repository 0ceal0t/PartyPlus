using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using FFXIVClientInterface;
using Newtonsoft.Json.Linq;
using PartyPlus.Helper;
using Dalamud.Game.Internal;
using FFXIVClientStructs.FFXIV.Component.GUI;
using FFXIVClientStructs.FFXIV.Client.Graphics;

#pragma warning disable CS0659
namespace PartyPlus {
    public unsafe class ParytPlus : IDalamudPlugin {
        public string Name => "PartyPlus";
        public DalamudPluginInterface PluginInterface { get; private set; }

        public string AssemblyLocation { get; private set; } = Assembly.GetExecutingAssembly().Location;

        internal Common Common;

        public static ClientInterface Client;

        private delegate void* ResetMemberPositionDelegate(IntPtr a1, float a2);
        private HookWrapper<ResetMemberPositionDelegate> resetMemberPositionHook;

        private delegate void* ResetChocoPositionDelegate(IntPtr a1, Int16 a2);
        private HookWrapper<ResetChocoPositionDelegate> resetChocotimeHook;

        private delegate void* ChangeMemberWidthDelegate(IntPtr a1, uint a2, float a3);
        private HookWrapper<ChangeMemberWidthDelegate> changeMemberWidthHook;

        public void Initialize(DalamudPluginInterface pluginInterface) {
            this.PluginInterface = pluginInterface;

            Client = new ClientInterface(pluginInterface.TargetModuleScanner, pluginInterface.Data); //
            UiHelper.Setup(pluginInterface.TargetModuleScanner);
            Common = new Common(pluginInterface);

            PluginInterface.UiBuilder.OnBuildUi += this.BuildUI;
            PluginInterface.Framework.OnUpdateEvent += FrameworkOnUpdate;

            var addr = Common.Scanner.ScanText("48 85 C9 74 24 8B 81 ?? ?? ?? ?? A8 01 75 15 F3 0F 10 41 ?? 0F 2E C1 7A 02 74 09 83 C8 01 89 81 ?? ?? ?? ?? F3 0F 11 49 48 C3 ");
            addr = IntPtr.Add(addr, 0x5);
            resetMemberPositionHook = Common.Hook<ResetMemberPositionDelegate>(addr, ResetMemberPositionDetour);

            changeMemberWidthHook = Common.Hook<ChangeMemberWidthDelegate>("48 89 5C 24 ?? 56 48 83 EC 30 48 89 7C 24 ?? 41 8B F0 48 63 FA 4C 8D 44 24 ?? ", ChangeMemberWidthDetour);
            resetChocotimeHook = Common.Hook<ResetChocoPositionDelegate>("0F BF C2 66 0F 6E C8 8B 81 ?? ?? ?? ?? 0F 5B C9 A8 01 75 15 F3 0F 10 41 ?? 0F 2E C1 7A 02 74 09 83 C8 01 89 81 ?? ?? ?? ?? F3 0F 11 49 48 C3", ResetChocoTimeDetour);

            SetupCommands();
        }

        public void Dispose() {
            PluginInterface.UiBuilder.OnBuildUi -= this.BuildUI;
            PluginInterface.Framework.OnUpdateEvent -= FrameworkOnUpdate;

            RemoveCommands();
            Client.Dispose();

            foreach (var hook in Common.HookList.Where(hook => !hook.IsDisposed)) {
                if (hook.IsEnabled) hook.Disable();
                hook.Dispose();
            }
            Common.HookList.Clear();
        }

        private void* ChangeMemberWidthDetour(IntPtr a1, uint a2, float a3) {
            return null; // I feel like this is going to bite me in the ass one day
            //return changeMemberWidthHook.Original(a1, a2, a3);
        }

        private void* ResetChocoTimeDetour(IntPtr a1, Int16 a2) {
            if (Horiz && a1 == ChocoTimeNode) {
                return null;
            }
            return resetChocotimeHook.Original(a1, a2);
        }

        private void* ResetMemberPositionDetour(IntPtr a1, float a2) {
            if (Horiz && PlayerNodes.Contains(a1)) { // % 40 == 0
                return null;
            }
            return resetMemberPositionHook.Original(a1, a2);
        }

        public void SetupCommands() {
            PluginInterface.CommandManager.AddHandler("/pplus", new Dalamud.Game.Command.CommandInfo(OnConfigCommandHandler) {
                HelpMessage = $"Open config window for {this.Name}",
                ShowInHelp = true
            });
        }

        public void OnConfigCommandHandler(object command, object args) {
        }

        public void RemoveCommands() {
            PluginInterface.CommandManager.RemoveHandler("/pplus");
        }

        private bool Visible = true;

        private bool ShowLvl = true;
        private bool Horiz = false;
        private bool BigHP = false;
        private bool ShowNumber = true;
        private bool ColorHealth = false;

        private void BuildUI() {
            ImGui.SetNextWindowSize(new Vector2(500, 500));
            if(ImGui.Begin("Settings", ref Visible)) {
                ImGui.Checkbox("Show Level", ref ShowLvl);
                ImGui.Checkbox("Show Number", ref ShowNumber);
                ImGui.Checkbox("Big HP", ref BigHP);
                ImGui.Checkbox("Health Color", ref ColorHealth);
                ImGui.Checkbox("Horizontal", ref Horiz);
                ImGui.End();
            }
        }

        private void FrameworkOnUpdate(Framework framework) {
            try {
                UpdateUI();
            }
            catch (Exception ex) {
                PluginLog.Log(ex.ToString());
            }
        }

        private static ByteColor OK_COLOR = new ByteColor {
            R = 255,
            G = 255,
            B = 255,
            A = 255
        };
        private static ByteColor WARNING_COLOR = new ByteColor {
            R = 255,
            G = 234,
            B = 8,
            A = 255
        };
        private static ByteColor DANGER_COLOR = new ByteColor {
            R = 250,
            G = 63,
            B = 42,
            A = 255
        };

        public HashSet<IntPtr> PlayerNodes = new HashSet<IntPtr>();
        public IntPtr ChocoTimeNode;

        public static int CHOCO_TIME_IDX = 21;

        public bool FirstTime = true;
        private unsafe void UpdateUI() {
            var pList = Common.GetUnitBase("_PartyList");
            if (pList == null) return;
            var node = pList->RootNode;
            if (node == null) return;

            var partyMemberBox = node->ChildNode->PrevSiblingNode->PrevSiblingNode->PrevSiblingNode;
            if (partyMemberBox->ChildCount != 13) return;

            // ========= SETUP ===========
            var memberNode = partyMemberBox->ChildNode;
            int NumVisible = 0;
            while (memberNode != null) {
                if (UiHelper.GetNodeVisible(memberNode)) NumVisible++;
                if (FirstTime) {
                    PlayerNodes.Add((IntPtr)memberNode);
                }
                memberNode = memberNode->PrevSiblingNode; // go from bottom of party list to top
            }
            if (FirstTime) {
                ChocoTimeNode = (IntPtr)pList->ULDData.NodeList[CHOCO_TIME_IDX];
                FirstTime = false;
            }

            // ==============

            int memberIdx = 0;
            int visibleIdx = 0;
            int chocoIdx = -1;
            memberNode = partyMemberBox->ChildNode; // reset

            while (memberNode != null) {
                var isSelf = (memberIdx == 0);
                var isPlayer = (memberIdx >= 5);
                var isChoco = (memberIdx == 1);
                if (isChoco) {
                    chocoIdx = visibleIdx;
                }

                if (memberNode->IsVisible) {
                    if (Horiz) {
                        UiHelper.SetPosition(memberNode, 250 * (NumVisible - visibleIdx - 1), 0);
                    }
                    else {
                        UiHelper.SetPosition(memberNode, 0, null);
                    }

                    var member = (AtkComponentNode*)memberNode;
                    var collisionNode = member->Component->ULDData.NodeList[0];
                    var hoverContainer = collisionNode->PrevSiblingNode;
                    var hoverNode = hoverContainer->ChildNode;
                    var clickFlashNode = hoverNode->PrevSiblingNode;
                    if (Horiz) {
                        UiHelper.SetSize(hoverNode, 215, 55);
                        UiHelper.SetSize(clickFlashNode, 215, 55);
                        UiHelper.SetSize(collisionNode, 259, 51);
                    }
                    else {
                        UiHelper.SetSize(hoverNode, 320, 48);
                        UiHelper.SetSize(clickFlashNode, 320, 48);
                        UiHelper.SetSize(collisionNode, 366, 44);
                    }

                    var statusNode = hoverContainer->PrevSiblingNode;
                    for(int i = 0; i < 10; i++) {
                        if (Horiz) {
                            UiHelper.SetPosition(statusNode, 25 + 25 * (9 - i), 70);
                        }
                        else {
                            UiHelper.SetPosition(statusNode, 263 + 25 * (9 - i), 12);
                        }
                        statusNode = statusNode->PrevSiblingNode;
                    }

                    var nameContainer = statusNode;
                    if (isPlayer) {
                        var nameText = nameContainer->ChildNode;
                        var numText = nameText->PrevSiblingNode;
                        if (ShowNumber) {
                            //UiHelper.Show(numText);
                        }
                        else {
                            //UiHelper.Hide(numText); // there's a function fucking with this
                        }

                        var mpContainer = nameContainer->PrevSiblingNode;
                        var healthContainer = mpContainer->PrevSiblingNode;

                        var health = (AtkComponentNode*)healthContainer;
                        var healthGaugeNode = (AtkComponentNode*)health->Component->ULDData.NodeList[0];
                        var healthBorderNode = healthGaugeNode->Component->ULDData.NodeList[0];
                        var healthBarNode = healthGaugeNode->Component->ULDData.NodeList[1];

                        if (BigHP) {
                        }
                        else {
                        }

                        var width = healthBarNode->Width;

                        if(!ColorHealth || width > 73) {
                            healthBarNode->Color = OK_COLOR;
                            healthBorderNode->Color = OK_COLOR;
                        }
                        else {
                            if (width < 47) {
                                healthBarNode->Color = DANGER_COLOR;
                                healthBorderNode->Color = DANGER_COLOR;
                            }
                            else {
                                healthBarNode->Color = WARNING_COLOR;
                                healthBorderNode->Color = WARNING_COLOR;
                            }
                        }
                    }

                    visibleIdx++;
                }

                memberNode = memberNode->PrevSiblingNode; // go from bottom of party list to top
                memberIdx++;
            }

            if (chocoIdx > -1) { // god I hate my life
                var chocoTimeNode = (AtkComponentNode*)pList->ULDData.NodeList[CHOCO_TIME_IDX];
                if (Horiz) {
                    UiHelper.SetPosition(chocoTimeNode, 250 * (NumVisible - chocoIdx - 1) + 150, 60);
                }
                else {
                    UiHelper.SetPosition(chocoTimeNode, 200, null);
                }
            }
        }
    }
}
