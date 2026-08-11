namespace Core.Configuration;

public class TlsOptions
{
    /// <summary>是否启用 TLS。false 时退化为明文 TCP，其余字段忽略。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>服务器证书文件路径（PFX / PKCS#12）。</summary>
    public string CertificatePath { get; set; } = "";

    /// <summary>证书文件密码；空字符串表示无密码。</summary>
    public string CertificatePassword { get; set; } = "";

    /// <summary>是否要求客户端提供证书（mTLS）。</summary>
    public bool RequireClientCertificate { get; set; } = false;

    /// <summary>TLS 握手截止时间，防止半开连接长期占用连接槽。</summary>
    public int HandshakeTimeoutMs { get; set; } = 10_000;

    /// <summary>启用 mTLS 时是否在线检查客户端证书吊销状态。</summary>
    public bool CheckClientCertificateRevocation { get; set; } = true;
}
