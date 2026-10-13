using System.Security.Cryptography;
using AgroMarket.Api.Data;
using Dapper;
using Microsoft.Extensions.Options;

namespace AgroMarket.Api.Federation.Infrastructure;

public class FedKeypairRow
{
    public string NodeCode { get; set; } = "";
    public string Alg { get; set; } = "RS256";
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
}

/// <summary>
/// 非对称令牌密钥存储 (单例): 本节点 RSA 密钥对的生成/读取, 以及对端公钥的存取。
/// 私钥仅存本节点库, 公钥通过 /api/federation/node/key 分发。
/// </summary>
public class FederationKeyStore
{
    private readonly IDbConnectionFactory _db;
    private readonly FederationOptions _opt;
    private readonly ILogger<FederationKeyStore> _logger;
    private readonly object _gate = new();
    private RSA? _privateKey;
    private string? _publicPem;

    public const string Alg = "RS256";
    public const int RsaKeySize = 2048;

    public FederationKeyStore(IDbConnectionFactory db, IOptions<FederationOptions> opt,
        ILogger<FederationKeyStore> logger)
    {
        _db = db;
        _opt = opt.Value;
        _logger = logger;
    }

    public FedKeypairRow? GetLocalKeypair()
    {
        using var conn = _db.Create();
        return conn.QuerySingleOrDefault<FedKeypairRow>(
            @"SELECT node_code AS NodeCode, alg AS Alg, public_key AS PublicKey, private_key AS PrivateKey
              FROM fed_keypair WHERE id=1");
    }

    /// <summary>确保本节点密钥对存在, 返回公钥 PEM (不存在则自动生成)。</summary>
    public string EnsureLocalKeypair()
    {
        lock (_gate)
        {
            if (_publicPem is null || _privateKey is null) LoadOrGenerate();
            return _publicPem!;
        }
    }

    /// <summary>获取用于签名的私钥 (不存在则自动生成并落库)。</summary>
    public RSA GetPrivateKey()
    {
        lock (_gate)
        {
            if (_privateKey is null) LoadOrGenerate();
            return _privateKey!;
        }
    }

    /// <summary>重新生成密钥对 (force=false 且已存在时报错), 返回新公钥 PEM。</summary>
    public string Regenerate(bool force)
    {
        lock (_gate)
        {
            if (!force && GetLocalKeypair() is not null)
                throw new InvalidOperationException("密钥对已存在; 如需覆盖请显式指定 force=true");
            var rsa = RSA.Create(RsaKeySize);
            SavePair(rsa);
            _privateKey = rsa;   // 保留不释放
            _publicPem = rsa.ExportSubjectPublicKeyInfoPem();
            _logger.LogWarning("已重新生成本节点非对称令牌密钥对 (node={Node})", _opt.NodeCode);
            return _publicPem;
        }
    }

    public string? GetPeerPublicKey(string nodeCode)
    {
        using var conn = _db.Create();
        return conn.ExecuteScalar<string?>(
            "SELECT public_key FROM fed_node WHERE node_code=@nodeCode", new { nodeCode });
    }

    public int SetPeerPublicKey(long nodeId, string publicPem)
    {
        using var conn = _db.Create();
        return conn.Execute(
            "UPDATE fed_node SET public_key=@publicPem, key_fetched_at=NOW(3) WHERE id=@nodeId",
            new { nodeId, publicPem });
    }

    // ---------------- 内部 ----------------

    private void LoadOrGenerate()
    {
        var row = GetLocalKeypair();
        if (row is not null && !string.IsNullOrWhiteSpace(row.PrivateKey))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(row.PrivateKey);
            _privateKey = rsa;
            _publicPem = row.PublicKey;
            return;
        }
        var fresh = RSA.Create(RsaKeySize);
        SavePair(fresh);
        _privateKey = fresh;
        _publicPem = fresh.ExportSubjectPublicKeyInfoPem();
        _logger.LogInformation("已生成本节点非对称令牌密钥对 (node={Node})", _opt.NodeCode);
    }

    private void SavePair(RSA rsa)
    {
        var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
        var privatePem = rsa.ExportPkcs8PrivateKeyPem();
        using var conn = _db.Create();
        conn.Execute(
            @"INSERT INTO fed_keypair (id, node_code, alg, public_key, private_key)
              VALUES (1, @nodeCode, @alg, @publicPem, @privatePem)
              ON DUPLICATE KEY UPDATE node_code=VALUES(node_code), alg=VALUES(alg),
                  public_key=VALUES(public_key), private_key=VALUES(private_key)",
            new { nodeCode = _opt.NodeCode, alg = Alg, publicPem, privatePem });
    }
}
