SSH Tunnel Manager for Windows

A lightweight Windows system tray app for managing SSH tunnels. Create, connect, and organize port forwarding rules with a clean native interface.

Windows 10+ | .NET 8 | WPF | License: GPLv3 | Download latest

## Install

### Manual Installation

Download the latest `SSHTunnel4Win-x.x.x-setup.exe` from the [Releases](https://github.com/TypoStudio/ssh-tunnel-for-win/releases) page and run it.

### Build from Source

```powershell
# Requires .NET 8 SDK
dotnet build SSHTunnel4Win.sln -c Release
```

## Features

### Tunnel Management
- Create and manage multiple SSH tunnel configurations
- Local (-L), Remote (-R), and Dynamic (-D) port forwarding
- Multiple forwarding rules per tunnel
- Connect / disconnect with a single click
- Auto-connect on launch per tunnel
- Disconnect on quit per tunnel
- Port conflict detection before connecting
- Real-time connection log viewer in a separate window

### SSH Config Integration
- Browse and edit `~/.ssh/config` hosts
- Load SSH Config hosts into tunnel configurations
- Open config files in external editor
- Raw text editing mode for SSH config entries

### Authentication
- Identity file (private key) selection
- Password stored securely via Windows DPAPI
- Additional SSH arguments support

### Share & Import
- Share tunnel configs as `sshtunnel://` URLs
- Import configs from share strings
- Copy equivalent CLI command (`ssh -L ...`)

### System Tray
- Quick connect / disconnect from the system tray
- Monitor running SSH processes
- Open Manager window from system tray
- Minimize to tray

### Localization
- English
- Korean (한국어)

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Ctrl+N | Add new tunnel |
| Ctrl+, | Open Settings |
| Alt+F4 | Quit application |

## Requirements

- Windows 10 or later
- SSH client (OpenSSH, bundled with Windows 10+)

## License

Copyright (c) 2026 TypoStudio (typ0s2d10@gmail.com)
https://github.com/TypoStudio/ssh-tunnel-for-win

SSH Tunnel Manager for Windows is available under the GNU General Public License v3.0.

---

## 한국어

Windows 시스템 트레이에서 SSH 터널을 간편하게 관리하는 네이티브 앱입니다.

### 설치

[Releases](https://github.com/TypoStudio/ssh-tunnel-for-win/releases) 페이지에서 최신 `SSHTunnel4Win-x.x.x-setup.exe`를 다운로드하고 실행하세요.

### 주요 기능

- **터널 관리** — 로컬(-L), 원격(-R), 다이나믹(-D) 포트 포워딩을 클릭 한 번으로 연결/해제, 실시간 연결 로그 확인
- **SSH Config 연동** — ~/.ssh/config 호스트를 탐색하고 터널 설정으로 불러오기
- **인증** — 인증 키 파일 선택, Windows DPAPI로 비밀번호 안전 저장
- **공유 및 가져오기** — sshtunnel:// URL로 설정 공유, 붙여넣기로 가져오기
- **시스템 트레이** — 시스템 트레이에서 빠른 연결/해제, SSH 프로세스 모니터링
- **다국어** — 영어, 한국어 지원

### 키보드 단축키

| 단축키 | 동작 |
|--------|------|
| Ctrl+N | 새 터널 추가 |
| Ctrl+, | 설정 열기 |
| Alt+F4 | 앱 종료 |

### 요구 사항

- Windows 10 이상
- SSH 클라이언트 (Windows 10 이상 기본 내장 OpenSSH)
