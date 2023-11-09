using System.Security.Cryptography;
using Bunkum.Core.Storage;
using JetBrains.Annotations;
using NotEnoughLogs;
using Refresh.GameServer.Authentication;
using Refresh.GameServer.Database;
using Refresh.GameServer.Time;
using Refresh.GameServer.Types.Assets;

namespace Refresh.GameServer.Importing;

public class AssetImporter : Importer
{
    private readonly IDateTimeProvider _timeProvider;
    
    public AssetImporter(Logger? logger = null, IDateTimeProvider? timeProvider = null) : base(logger)
    {
        timeProvider ??= new SystemDateTimeProvider();
        this._timeProvider = timeProvider;
    }

    public void ImportFromDataStore(GameDatabaseContext database, IDataStore dataStore)
    {
        int updatedAssets = 0;
        int newAssets = 0;
        this.Stopwatch.Start();
        
        IEnumerable<string> assetHashes = dataStore.GetKeysFromStore()
            .Where(key => !key.Contains('/'));

        List<GameAsset> assets = new();
        foreach (string hash in assetHashes)
        {
            byte[] data = dataStore.GetDataFromStore(hash);
            
            GameAsset? newAsset = this.ReadAndVerifyAsset(hash, data, null);
            if (newAsset == null) continue;

            GameAsset? oldAsset = database.GetAssetFromHash(hash);

            if (oldAsset != null)
            {
                newAsset.OriginalUploader = oldAsset.OriginalUploader;
                newAsset.UploadDate = oldAsset.UploadDate;
                updatedAssets++;
            }
            else
            {
                newAssets++;
            }

            assets.Add(newAsset);
            this.Info($"Processed {newAsset.AssetType} asset {hash} ({AssetSafetyLevelExtensions.FromAssetType(newAsset.AssetType)})");
        }
        
        database.AddOrUpdateAssetsInDatabase(assets);

        int hashCount = newAssets + updatedAssets;
        
        this.Info($"Successfully imported {assets.Count}/{hashCount} assets ({newAssets} new, {updatedAssets} updated) into database");
        if (assets.Count < hashCount)
        {
            this.Warn($"{hashCount - assets.Count} assets were not imported");
        }
    }

    private static string GetHashOfShaBytes(byte[] data)
    {
        return BitConverter.ToString(data)
            .Replace("-", "")
            .ToLower();
    }

    [Pure]
    public GameAsset? ReadAndVerifyAsset(string hash, byte[] data, TokenPlatform? platform)
    {
        string checkedHash = GetHashOfShaBytes(SHA1.HashData(data));

        if (checkedHash != hash)
        {
            this.Warn($"{hash} is actually hashed as {checkedHash} - this asset is likely corrupt.");
            return null;
        }

        GameAsset asset = new()
        {
            UploadDate = this._timeProvider.Now,
            OriginalUploader = null,
            AssetHash = hash,
            AssetType = this.DetermineAssetType(data, platform),
            IsPSP = platform == TokenPlatform.PSP,
            SizeInBytes = data.Length,
        };
        
        if (AssetTypeHasDependencyTree(asset.AssetType, data))
        {
            try
            {
                List<string> dependencies = this.ParseDependencyTree(data);
                foreach (string dependency in dependencies)
                {
                    asset.Dependencies.Add(dependency);
                }
            }
            catch (Exception e)
            {
                this.Warn($"Could not parse dependency tree for {hash}: {e}");
            }
        }

        return asset;
    }

    [Pure]
    private static bool AssetTypeHasDependencyTree(GameAssetType type, byte[] data)
    {
        if (type is GameAssetType.Jpeg
            or GameAssetType.Png
            or GameAssetType.Tga
            or GameAssetType.Texture
            or GameAssetType.GameDataTexture
            or GameAssetType.Unknown)
        {
            return false;
        }
        
        #if DEBUG
        char typeChar = (char)data[3];
        if (typeChar != 'b') throw new Exception($"Asset type {type} is not binary (char was '{typeChar}')");
        #endif

        return true;
    }

    private List<string> ParseDependencyTree(byte[] data)
    {
        List<string> dependencies = new();
        
        // Parse dependency table
        MemoryStream ms = new(data);
        BEBinaryReader reader = new(ms);

        ms.Seek(8, SeekOrigin.Begin);
        uint dependencyTableOffset = reader.ReadUInt32();

        ms.Seek(dependencyTableOffset, SeekOrigin.Begin);
        uint dependencyCount = reader.ReadUInt32();

        this.Debug($"Dependency count offset: {dependencyTableOffset}, count: {dependencyCount}");
        for (int i = 0; i < dependencyCount; i++)
        {
            byte flags = reader.ReadByte();
            if ((flags & 0x1) != 0) // UGC/SHA1
            {
                byte[] hashBytes = reader.ReadBytes(20);
                dependencies.Add(GetHashOfShaBytes(hashBytes));
            }
            else if ((flags & 0x2) != 0) reader.ReadUInt32(); // Skip GUID
                
            reader.ReadUInt32();
        }

        return dependencies;
    }
}