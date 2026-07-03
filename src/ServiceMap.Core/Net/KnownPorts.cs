namespace ServiceMap.Core.Net;

/// <summary>Well-known TCP/UDP service names so reports read in business terms.</summary>
public static class KnownPorts
{
    private static readonly Dictionary<int, string> Map = new()
    {
        [20] = "FTP-data", [21] = "FTP", [22] = "SSH", [23] = "Telnet",
        [25] = "SMTP", [53] = "DNS", [67] = "DHCP", [68] = "DHCP",
        [69] = "TFTP", [80] = "HTTP", [88] = "Kerberos", [110] = "POP3",
        [111] = "RPC", [123] = "NTP", [135] = "MS-RPC", [137] = "NetBIOS",
        [138] = "NetBIOS", [139] = "NetBIOS", [143] = "IMAP", [161] = "SNMP",
        [162] = "SNMP-trap", [389] = "LDAP", [443] = "HTTPS", [445] = "SMB",
        [464] = "Kerberos-pw", [514] = "Syslog", [587] = "SMTP-submit",
        [636] = "LDAPS", [989] = "FTPS-data", [990] = "FTPS", [993] = "IMAPS",
        [995] = "POP3S", [1080] = "SOCKS", [1194] = "OpenVPN", [1433] = "SQL Server",
        [1434] = "SQL Browser", [1521] = "Oracle", [1723] = "PPTP",
        [2049] = "NFS", [2181] = "ZooKeeper", [3268] = "Global Catalog",
        [3269] = "Global Catalog TLS", [3306] = "MySQL", [3389] = "RDP",
        [4369] = "EPMD", [5060] = "SIP", [5432] = "PostgreSQL", [5671] = "AMQP-TLS",
        [5672] = "AMQP", [5985] = "WinRM-HTTP", [5986] = "WinRM-HTTPS",
        [6379] = "Redis", [7001] = "WebLogic", [8080] = "HTTP-alt",
        [8443] = "HTTPS-alt", [9042] = "Cassandra", [9092] = "Kafka",
        [9200] = "Elasticsearch", [11211] = "Memcached", [27017] = "MongoDB"
    };

    public static string? Name(int port) => Map.TryGetValue(port, out var n) ? n : null;

    /// <summary>"443 (HTTPS)" when known, otherwise "443".</summary>
    public static string Describe(int port)
    {
        var n = Name(port);
        return n is null ? port.ToString() : $"{port} ({n})";
    }
}
