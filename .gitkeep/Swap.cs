﻿using Galaxy_Swapper_v2.Workspace.CProvider;
using Galaxy_Swapper_v2.Workspace.CProvider.Encryption;
using Galaxy_Swapper_v2.Workspace.Generation.Formats;
using Galaxy_Swapper_v2.Workspace.Properties;
using Galaxy_Swapper_v2.Workspace.Swapping.Sterilization;
using Galaxy_Swapper_v2.Workspace.Usercontrols.Overlays;
using Galaxy_Swapper_v2.Workspace.Utilities;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Galaxy_Swapper_v2.Workspace.Global;

namespace Galaxy_Swapper_v2.Workspace.Swapping
{
    public class Swap
    {
        private SwapView SwapView { get; set; } = default!;
        private FovView FovView { get; set; } = default!;
        private Asset Asset { get; set; } = default!;
        public Swap(SwapView swapview, FovView fovview, Asset asset)
        {
            SwapView = swapview;
            FovView = fovview;
            Asset = asset;
        }

        private void Output(string Content, SwapView.Type Type)
        {
            if (FovView == null)
                SwapView.Output(Content, Type);
            else
                FovView.Output(Content, (FovView.Type)Type);
        }

        public bool Convert()
        {
            var Deserializer = new Deserializer(Asset.Export.UncompressedBuffer);

            Output(Languages.Read(Languages.Type.View, "SwapView", "Deserializing"), SwapView.Type.Info);
            Deserializer.Read();

            if (Asset.OverrideObject != null && !string.IsNullOrEmpty(Asset.OverrideObject))
            {
                var OverrideDeserializer = new Deserializer(Asset.OverrideExport.UncompressedBuffer);
                OverrideDeserializer.Read();

                OverrideDeserializer.ReplaceEntry(Deserializer, Asset.OverrideObject.Split('.')[0], Asset.Object.Split('.')[0]);
                OverrideDeserializer.ReplaceEntry(Deserializer, System.IO.Path.GetFileNameWithoutExtension(Asset.OverrideObject), System.IO.Path.GetFileNameWithoutExtension(Asset.Object));

                ulong publicExportHash = Deserializer.ExportMap[Deserializer.ExportMap.Length - 1].PublicExportHash;
                OverrideDeserializer.ExportMap[OverrideDeserializer.ExportMap.Length - 1].PublicExportHash = publicExportHash;

                Deserializer = OverrideDeserializer;
            }

            if (Asset.Swaps != null)
            {
                Output(Languages.Read(Languages.Type.View, "SwapView", "SwappingStrings"), SwapView.Type.Info);

                foreach (var Object in Asset.Swaps)
                {
                    switch (Object["type"].Value<string>())
                    {
                        case "string":
                            {
                                string ObjectPath = (Object["search"].Value<string>().Contains('.') ? Object["search"].Value<string>().Split('.').First() : Object["search"].Value<string>()).Replace("FortniteGame/Plugins/GameFeatures/BRCosmetics/Content/", "/BRCosmetics/");
                                string ObjectName = Object["search"].Value<string>().Split('.')?.Last();
                                string OverrideObjectPath = (Object["replace"].Value<string>().Contains('.') ? Object["replace"].Value<string>().Split('.').First() : Object["replace"].Value<string>()).Replace("FortniteGame/Plugins/GameFeatures/BRCosmetics/Content/", "/BRCosmetics/");
                                string OverrideObjectName = Object["replace"].Value<string>().Split('.')?.Last();

                                if (!string.IsNullOrEmpty(OverrideObjectPath) && !Object["UEFN"].KeyIsNullOrEmpty() && Object["UEFN"].Value<bool>())
                                {
                                    OverrideObjectPath = CProviderManager.FormatUEFNGamePath(OverrideObjectPath);
                                }

                                Deserializer.ReplaceEntry(ObjectPath, OverrideObjectPath);

                                if (!string.IsNullOrEmpty(ObjectName) && !string.IsNullOrEmpty(OverrideObjectName))
                                    Deserializer.ReplaceEntry(ObjectName, OverrideObjectName);
                            }
                            break;
                        case "tag":
                            {
                                Deserializer.ReplaceEntry(Object["search"].Value<string>(), Object["replace"].Value<string>());
                            }
                            break;
                    }
                }
            }

            Output(Languages.Read(Languages.Type.View, "SwapView", "Sterilizing"), SwapView.Type.Info);
            List<byte> Buffer = new List<byte>(new Serializer(Deserializer).Write());

            if (Asset.Swaps != null)
            {
                Output(Languages.Read(Languages.Type.View, "SwapView", "SwappingHex"), SwapView.Type.Info);

                foreach (var Object in Asset.Swaps)
                {
                    if (Object["type"].Value<string>() != "hex")
                        continue;

                    byte[] Search = Misc.HexToByte(Object["search"].Value<string>());
                    byte[] Replace = Misc.HexToByte(Object["replace"].Value<string>());

                    int HexPos = 0;

                    if (!Object["SearchAfter"].KeyIsNullOrEmpty())
                    {
                        byte[] SearchAfter = Misc.HexToByte(Object["SearchAfter"]["hex"].Value<string>());
                        HexPos = Buffer.ToArray().IndexOfSequence(SearchAfter, 0);
                        HexPos = HexPos == -1 ? 0 : HexPos;
                    }

                    HexPos = Buffer.ToArray().IndexOfSequence(Search, HexPos);

                    if (HexPos > 0)
                    {
                        Buffer.RemoveRange(HexPos, Search.Length);
                        Buffer.InsertRange(HexPos, Replace);
                    }

                    Log.Information("Replaced hex");
                }
            }

            byte[] BufferToWrite = Buffer.ToArray();

            if (Asset.Export.IsEncrypted)
            {
                BufferToWrite = UnrealAes.Encrypt(BufferToWrite, CProviderManager.DefaultProvider.Keys[Asset.Export.IoStoreTocHeader.EncryptionKeyGuid].Key);
            }

            string Ucas = $"{Settings.Read(Settings.Type.Installtion).Value<string>()}\\FortniteGame\\Content\\Paks\\{Asset.Export.LastUcas}.ucas";
            string Utoc = $"{Settings.Read(Settings.Type.Installtion).Value<string>()}\\FortniteGame\\Content\\Paks\\{Asset.Export.Utoc}.utoc";

            Output(Languages.Read(Languages.Type.View, "SwapView", "Preparing"), SwapView.Type.Info);

            if (!Ucas.CanEdit())
                throw new CustomException($"Failed to apply the swap due to the following file being in use!\n{Ucas}\nPlease ensure Fortnite or any other program is not using your game files.");
            if (!Utoc.CanEdit())
                throw new CustomException($"Failed to apply the swap due to the following file being in use!\n{Utoc}\nPlease ensure Fortnite or any other program is not using your game files.");

            long position = 0;
            using (BinaryWriter UcasEdit = new BinaryWriter(File.Open(Ucas, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)))
            {
                position = UcasEdit.BaseStream.Length;
                UcasEdit.BaseStream.Seek(position, SeekOrigin.Begin);
                UcasEdit.Write(BufferToWrite, 0, BufferToWrite.Length);
                UcasEdit.Close();

                Log.Information($"Wrote to: {Ucas} at Offset: {position}");
            }

            Output(string.Format(Languages.Read(Languages.Type.View, "SwapView", "Wrote"), "ucas"), SwapView.Type.Info);

            using (BinaryWriter UtocEdit = new BinaryWriter(File.Open(Utoc, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)))
            {
                UtocEdit.Seek((int)Asset.Export.CompressionBlock.Position, SeekOrigin.Begin);
                UtocEdit.Write(Misc.CompressionBlock((uint)position, (uint)BufferToWrite.Length, (uint)BufferToWrite.Length, (uint)Asset.Export.LastPartition, false), 0, 12);

                Log.Information($"Wrote to: {Utoc} at Offset: {Asset.Export.CompressionBlock.Position}");

                UtocEdit.Seek((int)Asset.Export.ChunkOffsetLengths.Position + 5, SeekOrigin.Begin);
                UtocEdit.Write(Misc.AssetLengthBlock(BufferToWrite.Length), 0, 5);

                Log.Information($"Wrote to: {Utoc} at Offset: {Asset.Export.ChunkOffsetLengths.Position + 5}");

                UtocEdit.Close();
            }

            Output(string.Format(Languages.Read(Languages.Type.View, "SwapView", "Wrote"), "utoc"), SwapView.Type.Info);

            return true;
        }

        public bool Revert()
        {
            Output(Languages.Read(Languages.Type.View, "SwapView", "Preparing"), SwapView.Type.Info);

            string Utoc = $"{Settings.Read(Settings.Type.Installtion).Value<string>()}\\FortniteGame\\Content\\Paks\\{Asset.Export.Utoc}.utoc";
            using (BinaryWriter UtocEdit = new BinaryWriter(File.Open(Utoc, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)))
            {
                Output(Languages.Read(Languages.Type.View, "SwapView", "Overwriting"), SwapView.Type.Info);

                UtocEdit.BaseStream.Position = Asset.Export.CompressionBlock.Position;
                UtocEdit.Write(Asset.Export.CompressionBlock.Buffer, 0, 12);

                Log.Information($"Wrote to: {Utoc} at Offset: {Asset.Export.CompressionBlock.Position}");

                UtocEdit.BaseStream.Position = Asset.Export.ChunkOffsetLengths.Position;
                UtocEdit.Write(Asset.Export.ChunkOffsetLengths.Buffer, 0, 12);

                Log.Information($"Wrote to: {Utoc} at Offset: {Asset.Export.ChunkOffsetLengths.Position}");

                UtocEdit.Close();
            }

            Output(string.Format(Languages.Read(Languages.Type.View, "SwapView", "Wrote"), "utoc"), SwapView.Type.Info);

            return true;
        }
    }
}