# Aeon Remote Support Installer

This tool installs and configures [NetBird](https://netbird.io) and [RustDesk](https://rustdesk.com) to enable **secure, remote desktop access** between Aeon Laboratories and customer systems. It is intended for customers who use Aeon's instrument control platform and need remote support or remote access capability.

## ✨ Features

- 👨‍💻 Installs and configures NetBird VPN
- 🖥 Installs and preconfigures RustDesk remote desktop
- 🔐 Communicates securely with Aeon's internal services
- 🧹 Supports two roles:
  - **Subscribers** (for systems managed by Aeon)
  - **Guests** (for users who want access to their own hosts)

## 🔧 Building the Installer

This is a .NET console app. Before building, you must supply your own configuration keys.

### 1. Provide Secrets

Before building, create a file named:

```
Program.Secrets.cs
```

Use the provided template:

```
Program.Secrets.Template.cs
```

and fill in the required values:

- `NetbirdGuestKey` – setup key for guest machines
- `NetbirdSubscriberKey` – setup key for subscriber (Aeon-managed) machines
- `MgmtUrl` – NetBird management service URL
- `RustdeskKey` – RustDesk server public key
- `ValetUrl` – Aeon internal service endpoint (over VPN)

**🚫 Do not commit **``** — it is excluded by **``**.**

### 2. Build a self-contained release

```bash
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The resulting `RemoteSupportInstaller.exe` is a single-file executable suitable for distribution.

---

## 🖥 Usage

To install the Aeon Remote Support stack:

```bash
RemoteSupportInstaller.exe
```

To install as a **guest** (for users who want to access their host from another machine):

```bash
RemoteSupportInstaller.exe --guest
```

To see all options:

```bash
RemoteSupportInstaller.exe --help
```

---

## 🔐 Security Considerations

This installer embeds live secrets (e.g., NetBird setup keys, RustDesk server key) in order to simplify deployment. These secrets allow systems to register with Aeon's VPN and remote support infrastructure but do not expose administrative privileges or critical backend access.

- For **maximum security**, all secrets can be supplied via command-line arguments at runtime.
- In practice, we accept the minimal risk of embedding keys in the installer and monitor usage accordingly.
- The `ValetUrl` is only accessible over VPN, and other embedded keys are scoped to their purpose.
- If needed, you can rotate any exposed key using the NetBird or RustDesk management interface.

While the consequences of exposure are limited, users are encouraged to understand the implications of distributing preconfigured installers and to rotate keys if they suspect misuse.

---

## 📜 License

This project is licensed under the **GNU GPL v3**. See the [LICENSE](LICENSE) file for details.

---

## 🤝 Contributions

This repository is maintained by [Aeon Laboratories](https://www.aeonlaboratories.com/). Issues, feedback, and forks are welcome — please review your changes for security implications before publishing.

