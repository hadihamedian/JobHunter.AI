# 🎯 JobHunter.AI

**A hybrid, local-first AI career assistant — resume analysis, interview prep, and application tracking with zero hallucinations.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Blazor](https://img.shields.io/badge/Blazor-WASM-512BD4?logo=blazor)](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor)
[![Microsoft.Extensions.AI](https://img.shields.io/badge/Microsoft.Extensions.AI-Tool%20Calling-512BD4?logo=dotnet)](https://learn.microsoft.com/en-us/dotnet/ai/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Database-4169E1?logo=postgresql)](https://www.postgresql.org/)
[![Ollama](https://img.shields.io/badge/Ollama-Local%20LLM-000000?logo=ollama)](https://ollama.com/)
[![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker)](https://www.docker.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/hadihamedian/JobHunter.AI)

---

## 🧭 Overview

JobHunter.AI is an end-to-end career management platform built to solve a very practical problem: **generic AI resume tools hallucinate, and generic ATS tools are dumb**. This project takes a hybrid approach — deterministic, rule-based logic where accuracy matters (skill matching, scoring, structural validation) combined with a **local LLM (via Ollama)** orchestrated through **Microsoft.Extensions.AI and Agent Skills** for the parts that genuinely benefit from generative reasoning.

Everything runs **locally by default** — no data ever has to leave your machine, no API keys, no per-token cost. It's both a daily-use tool for managing a job search and a demonstration of applied .NET + Agentic AI architecture: Minimal APIs, Blazor WebAssembly, PostgreSQL, and strict tool invocation pipelines.

---

📸 Screenshots
| Smart Analyze | Career Chat |
| :---: | :---: |
| ![Smart Analyze](.screenshots/Smart-Analyze.png) | ![Career Chat](.screenshots/Career-Chat.png) |

| Applications Dashboard | Resume Tailor |
| :---: | :---: |
| ![Dashboard](.screenshots/Dashboard.png) | ![Resume Tailor](.screenshots/Resume-Tailor.png) |

---

## ✨ Key Features

| Module | What it does |
|---|---|
| 🧠 **Smart Analyze** | Deterministic resume-to-job-description matching — verifiable skill-gap detection with zero LLM guessing |
| 💬 **Career Chat** | Conversational advisor with on-demand access to your career records via Agent Skills |
| 📊 **Applications Dashboard** | Track every application with AI-powered semantic search and direct database query tools |
| 📄 **Resume Manager** | Store, organize, and version multiple resumes with AI-assisted recommendation |
| 🎯 **Resume Tailor** | Self-correcting generative rewriter that ensures 100% structural parity with the original resume |
| 🗣️ **Interview Prep** | Realistic, role-specific technical and behavioral questions generated directly from job descriptions |
| 🏦 **Interview Bank** | Filterable, searchable repository of past questions with natural language navigation |

---

## 🏗️ Architecture

JobHunter.AI decouples deterministic verification from generative reasoning using **Microsoft.Extensions.AI**:

```
┌──────────────────────────────────────────────────────────────┐
│                        Nginx (reverse proxy)                  │
│                     serves Blazor WASM + /api/* → api         │
└───────────────────────────────┬────────────────────────────────┘
                                 │
                ┌────────────────┴─────────────────┐
                │                                   │
┌───────────────▼───────────┐        ┌──────────────▼───────────────┐
│   JobHunter.AI.Web          │  HTTP  │      JobHunter.AI.Api          │
│   (Blazor WebAssembly)      │◄──────►│      (ASP.NET Core Minimal)   │
│                             │        │                                │
│  Pages:                     │        │  Agent Skills & Tools:         │
│   • Applications            │        │   • AtsEvaluationSkills        │
│   • CareerChat              │        │   • ApplicationDataSkills      │
│   • Resumes                 │        │   • InterviewBankSkills        │
│   • ResumeTailor            │        │   • CareerAdvisorSkills        │
│   • InterviewBank           │        │                                │
│                             │        │  Core IChatClient Pipeline     │
│                             │        │  (Function Invocation Layer)   │
└─────────────────────────────┘        └───────────────┬────────────────┘
                                                        │
                                            ┌───────────▼────────────┐
                                            │   Ollama (local LLM)     │
                                            │   llama3.2:latest        │
                                            │   (runs on the host)     │
                                            └─────────────────────────┘
                                                        │
                                            ┌───────────▼────────────┐
                                            │      PostgreSQL          │
                                            │      (Docker container)  │
                                            └─────────────────────────┘
```

**Design Principle:** High-risk logic (keyword checking, structural parity, database filtering, ID resolution) stays in pure C# skills. The LLM acts purely as an agent that selects and executes tools or generates structured content constrained by JSON Schemas.

---

## 🛠️ Tech Stack

- **Backend:** ASP.NET Core Minimal API (.NET 10)
- **Agentic Abstraction:** `Microsoft.Extensions.AI` with automated tool invocation (`AIFunctionFactory`)
- **Frontend:** Blazor WebAssembly
- **Database:** PostgreSQL (via Npgsql)
- **Local AI Engine:** [Ollama](https://ollama.com/) running `llama3.2:latest`
- **Reverse Proxy:** Nginx (serves the WASM app, proxies `/api/*` to backend)
- **Containerization:** Docker & Docker Compose

---

## 🚀 Getting Started

### Option A — Docker (recommended)

#### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) & Docker Compose
- [Ollama](https://ollama.com/download) installed **on the host machine**, with the model pulled:
  ```bash
  ollama pull llama3.2:latest
  ```

#### Run

```bash
# Clone the repository
git clone https://github.com/hadihamedian/JobHunter.AI.git
cd JobHunter.AI

# Build and start services (API, Web, PostgreSQL)
docker compose up --build -d
```

The app will be available at **http://localhost:5150** (or port 80/configured proxy port).

---

### Option B — Manual (without Docker)

#### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [PostgreSQL](https://www.postgresql.org/download/)
- [Ollama](https://ollama.com/download) with `llama3.2:latest`

#### Configuration (`JobHunter.AI.Api/appsettings.json`)

```json
{
  "Ollama": {
    "Endpoint": "http://localhost:11434",
    "Model": "llama3.2:latest"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=jobassistant;Username=postgres;Password=postgres"
  }
}
```

#### Run

```bash
# Start API
cd JobHunter.AI.Api
dotnet run

# In another terminal, start Blazor Web
cd JobHunter.AI.Web
dotnet run
```

---

## 📂 Project Structure

```
JobHunter.AI/
├── JobHunter.AI.Api/            # Backend - Minimal API (.NET 10)
│   ├── Models/                  # DTOs and contracts
│   ├── Skills/                  # Agent Skills & Native Tools (Ats, Data, Interview, Career)
│   ├── Services/                # Business orchestration using IChatClient
│   ├── Dockerfile
│   └── Program.cs
│
├── JobHunter.AI.Web/            # Frontend - Blazor WASM
│   ├── Pages/                   # Applications, CareerChat, Resumes, ResumeTailor, InterviewBank
│   ├── Layout/
│   ├── Services/                # HTTP API clients
│   └── wwwroot/
│
├── docker-compose.yml           # API + Web + PostgreSQL orchestration
└── JobHunter.AI.slnx
```

---

## 📜 License

Licensed under the [MIT License](./LICENSE).

---

## 👤 Author

**Hadi Hamedian** — Senior .NET Developer & Technical Lead  
Building AI-integrated .NET systems, remote-first.

[GitHub](https://github.com/hadihamedian)