<div align="center">
    <img src="SSHTunnel4Win/Assets/app-icon.ico" width="200" height="200">
    <h1>SSH Tunnel Manager</h1>
</div>

A lightweight Windows system tray app for managing SSH tunnels. Create, connect, and organize port forwarding rules with a clean native interface.

![Windows](https://img.shields.io/badge/Windows-10%2B-0078D6?style=flat-square&logo=windows)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet)
![License](https://img.shields.io/badge/license-GPLv3-blue?style=flat-square)
[![Download](https://img.shields.io/badge/download-latest-brightgreen?style=flat-square)](https://github.com/TypoStudio/ssh-tunnel-for-win/releases)

## Install

### Installer

Download the latest `SSHTunnel4Win-x.x.x-setup.exe` from the [Releases](https://github.com/TypoStudio/ssh-tunnel-for-win/releases) page and run it.

### Build from Source

```powershell
# Requires .NET 8 SDK
dotnet build SSHTunnel4Win.sln -c Release
```

## Features

### Tunnel Management
- [x] Create and manage multiple SSH tunnel configurations
- [x] Local (`-L`), Remote (`-R`), and Dynamic (`-D`) port forwarding
- [x] Multiple forwarding rules per tunnel
- [x] Connect / disconnect with a single click
- [x] Auto-connect on launch per tunnel
- [x] Disconnect on quit per tunnel
- [x] Port conflict detection before connecting
- [x] Real-time connection log viewer in a separate window

### SSH Config Integration
- [x] Browse and edit `~/.ssh/config` hosts
- [x] Load SSH Config hosts into tunnel configurations
- [x] Open config files in external editor
- [x] Raw text editing mode for SSH config entries

### Authentication
- [x] Identity file (private key) selection
- [x] Password stored securely via Windows DPAPI
- [x] Additional SSH arguments support

### Share & Import
- [x] Share tunnel configs as `sshtunnel://` URLs
- [x] Import configs from share strings
- [x] Copy equivalent CLI command (`ssh -L ...`)

### System Tray
- [x] Quick connect / disconnect from the system tray
- [x] Monitor running SSH processes
- [x] Open Manager window from system tray
- [x] Minimize to tray

### Localization
- [x] English
- [x] Korean (한국어)

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+N` | Add new tunnel |
| `Ctrl+,` | Open Settings |
| `Alt+F4` | Quit application |

## Requirements

- Windows 10 or later
- SSH client (OpenSSH, bundled with Windows 10+)

## Also Available

Looking for the macOS version? Check out [SSH Tunnel Manager for macOS](https://github.com/TypoStudio/ssh-tunnel-for-macos).

## License

Copyright (c) 2026 TypoStudio (typ0s2d10@gmail.com)\
https://github.com/TypoStudio/ssh-tunnel-for-win

SSH Tunnel Manager for Windows is available under the [GNU General Public License v3.0](LICENSE).

<a href="https://www.buymeacoffee.com/typ0s2d10" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/arial-yellow.png" alt="Buy Me A Coffee" style="height: 60px !important;width: 217px !important;" ></a>

---

## 한국어

Windows 시스템 트레이에서 SSH 터널을 간편하게 관리하는 네이티브 앱입니다.

### 설치

[Releases](https://github.com/TypoStudio/ssh-tunnel-for-win/releases) 페이지에서 최신 `SSHTunnel4Win-x.x.x-setup.exe`를 다운로드하고 실행하세요.

### 주요 기능

- **터널 관리** — 로컬(`-L`), 원격(`-R`), 다이나믹(`-D`) 포트 포워딩을 클릭 한 번으로 연결/해제, 실시간 연결 로그 확인
- **SSH Config 연동** — `~/.ssh/config` 호스트를 탐색하고 터널 설정으로 불러오기
- **인증** — 인증 키 파일 선택, Windows DPAPI로 비밀번호 안전 저장
- **공유 및 가져오기** — `sshtunnel://` URL로 설정 공유, 붙여넣기로 가져오기
- **시스템 트레이** — 시스템 트레이에서 빠른 연결/해제, SSH 프로세스 모니터링
- **설정** — 시작 시 자동 실행, 시작 시 매니저 열기
- **다국어** — 영어, 한국어 지원

### 키보드 단축키

| 단축키 | 동작 |
|--------|------|
| `Ctrl+N` | 새 터널 추가 |
| `Ctrl+,` | 설정 열기 |
| `Alt+F4` | 앱 종료 |

### 요구 사항

- Windows 10 이상
- SSH 클라이언트 (Windows 10 이상 기본 내장 OpenSSH)

### macOS 버전

macOS 버전은 [SSH Tunnel Manager for macOS](https://github.com/TypoStudio/ssh-tunnel-for-macos)에서 확인하세요.
