using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace PartyPlus.Helper {
    internal unsafe class Common {
        public static DalamudPluginInterface PluginInterface { get; private set; }

        private delegate IntPtr GameAlloc(ulong size, IntPtr unk, IntPtr allocator, IntPtr alignment);

        private delegate IntPtr GetGameAllocator();

        private static GameAlloc _gameAlloc;
        private static GetGameAllocator _getGameAllocator;

        public static IntPtr PlayerStaticAddress { get; private set; }

        public static SigScanner Scanner => PluginInterface.TargetModuleScanner;

        public Common(DalamudPluginInterface pluginInterface) {
            PluginInterface = pluginInterface;
            var gameAllocPtr = pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 45 8D 67 23");
            var getGameAllocatorPtr = pluginInterface.TargetModuleScanner.ScanText("E8 ?? ?? ?? ?? 8B 75 08");

            PlayerStaticAddress = pluginInterface.TargetModuleScanner.GetStaticAddressFromSig("8B D7 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 0F B7 E8");

            _gameAlloc = Marshal.GetDelegateForFunctionPointer<GameAlloc>(gameAllocPtr);
            _getGameAllocator = Marshal.GetDelegateForFunctionPointer<GetGameAllocator>(getGameAllocatorPtr);
        }

        public static AtkUnitBase* GetUnitBase(string name, int index = 1) {
            return (AtkUnitBase*) PluginInterface.Framework.Gui.GetUiObjectByName(name, index);
        }

        public static IntPtr Alloc(ulong size) {
            if (_gameAlloc == null || _getGameAllocator == null) return IntPtr.Zero;
            return _gameAlloc(size, IntPtr.Zero, _getGameAllocator(), IntPtr.Zero);
        }
        
        public void WriteSeString(byte** startPtr, IntPtr alloc, SeString seString) {
            if (startPtr == null) return;
            var start = *(startPtr);
            if (start == null) return;
            if (start == (byte*)alloc) return;
            WriteSeString((byte*)alloc, seString);
            *startPtr = (byte*)alloc;
        }

        public SeString ReadSeString(byte** startPtr) {
            if (startPtr == null) return null;
            var start = *(startPtr);
            if (start == null) return null;
            return ReadSeString(start);
        }

        public SeString ReadSeString(byte* ptr) {
            var offset = 0;
            while (true) {
                var b = *(ptr + offset);
                if (b == 0) {
                    break;
                }
                offset += 1;
            }
            var bytes = new byte[offset];
            Marshal.Copy(new IntPtr(ptr), bytes, 0, offset);
            return PluginInterface.SeStringManager.Parse(bytes);
        }

        public void WriteSeString(byte* dst, SeString s) {
            var bytes = s.Encode();
            for (var i = 0; i < bytes.Length; i++) {
                *(dst + i) = bytes[i];
            }
            *(dst + bytes.Length) = 0;
        }

        public void WriteSeString(Utf8String xivString, SeString s) {
            var bytes = s.Encode();
            int i;
            for (i = 0; i < bytes.Length && i < xivString.BufSize - 1; i++) {
                *(xivString.StringPtr + i) = bytes[i];
            }
            *(xivString.StringPtr + i) = 0;
        }

        public enum GameOptionKind : uint {
            GamePadMode        = 0x089, // [bool] Character Config -> Mouse Mode / GamePad Mode
            LegacyMovement     = 0x08A, // [bool] Character Config -> Control Settings -> General -> Standard Type / Legacy Type
            DisplayItemHelp    = 0x130, // [bool] Character Config -> UI Settings -> General -> Display Item Help
            DisplayActionHelp  = 0x136, // [bool] Character Config -> UI Settings -> General -> Display Action Help

            ClockDisplayType   = 0x153, // [enum/byte] 0 = Default, 1 = 24H, 2 = 12H 
            ClockTypeEorzea    = 0x155, // [bool]
            ClockTypeLocal     = 0x156, // [bool]
            ClockTypeServer    = 0x157, // [bool]
        }


        public T GetGameOption<T>(GameOptionKind opt) {
            var optionBase = (byte**)(PluginInterface.Framework.Address.BaseAddress + 0x2B28);
            return Marshal.PtrToStructure<T>(new IntPtr(*optionBase + 0xAAE0 + (16 * (uint)opt)));
        }

        public static HookWrapper<T> Hook<T>(string signature, T detour, bool enable = true) where T : Delegate {
            var addr = Common.Scanner.ScanText(signature);
            var h = new Hook<T>(addr, detour);
            var wh = new HookWrapper<T>(h);
            if (enable) wh.Enable();
            HookList.Add(wh);
            return wh;
        }

        public static HookWrapper<T> Hook<T>(IntPtr addr, T detour, bool enable = true) where T : Delegate {
            var h = new Hook<T>(addr, detour);
            var wh = new HookWrapper<T>(h);
            if (enable) wh.Enable();
            HookList.Add(wh);
            return wh;
        }

        public static List<IHookWrapper> HookList = new();

    }
}
