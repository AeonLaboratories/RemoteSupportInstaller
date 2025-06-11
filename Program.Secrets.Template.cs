// Program.Secrets.Template.cs
//
// This is a template for 'Program.Secrets.cs'. Edit the strings in that
// file to reflect your actual configured values.
//
// NetbirdGuestKey and NetbirdSubscriberKey are setup keys created at the NetBird dashboard,
// intended to assign the peer to an appropriate group. If this discrimination is not needed,
// the two values can be identical.
//
// MgmtUrl is the public-facing URL of NetBird's management service.
//
// RustdeskKey is found in the id_ed25519.pub file created when the RustDesk server is set up.
//
// ValetUrl is the VPN domain name assigned to the RustDesk server.
//
public static partial class Program
{
    public static readonly string NetbirdGuestKey = "<ENTER-GUEST-KEY>";
    public static readonly string NetbirdSubscriberKey = "<ENTER-SUBSCRIBER-KEY>";
    public static readonly string MgmtUrl = "https://<netbird-management>:<port>";
    public static readonly string RustdeskKey = "<ENTER-RUSTDESK-PUBLIC-KEY>";
    public static readonly string ValetUrl = "http://<valet-vpn-url>:<port>";
}
