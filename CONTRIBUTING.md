# Contributing to DeepFocus for Windows

Thank you for your interest in contributing to DeepFocus for Windows! This document provides guidelines and information for contributors.

## 📋 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [Getting Started](#getting-started)
- [Development Setup](#development-setup)
- [Development Workflow](#development-workflow)
- [Testing](#testing)
- [Code Style](#code-style)
- [Pull Request Process](#pull-request-process)
- [Release Process](#release-process)

## Code of Conduct

Be respectful, constructive, and professional in all interactions. We aim to maintain a welcoming environment for all contributors.

## Getting Started

### Prerequisites

- **Windows 10/11**: This is a Windows-specific application
- **.NET 10 SDK**: [Download here](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Git**: For version control
- **IDE**: Visual Studio 2022, Rider, or VS Code with C# extensions

### Fork and Clone

1. Fork the repository on GitHub
2. Clone your fork locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/DeepFocusForWindows.git
   cd DeepFocusForWindows
   ```
3. Add the upstream repository:
   ```bash
   git remote add upstream https://github.com/ORIGINAL-OWNER/DeepFocusForWindows.git
   ```

## Development Setup

### Building the Project

```bash
# Restore dependencies
dotnet restore

# Build the solution
dotnet build DeepFocusForWindows.sln

# Run the application
dotnet run --project DeepFocusForWindows/DeepFocusForWindows.csproj
```

### Project Structure

```
DeepFocusForWindows/
├── DeepFocusForWindows/          # Main application
│   ├── App.axaml                 # Application entry point
│   ├── Models/                   # Data models
│   ├── ViewModels/               # MVVM view models
│   ├── Views/                    # UI views
│   ├── Services/                 # Business logic and services
│   ├── Native/                   # Win32 API interop
│   └── Assets/                   # Images, icons, etc.
├── DeepFocusForWindows.Tests/    # Unit tests
└── .github/workflows/            # CI/CD workflows
```

## Development Workflow

### Creating a Feature Branch

```bash
# Update your main branch
git checkout master
git pull upstream master

# Create a feature branch
git checkout -b feature/your-feature-name
```

### Making Changes

1. Make your changes in small, logical commits
2. Test your changes thoroughly
3. Ensure all tests pass
4. Update documentation if needed

### Technology Stack

- **Framework**: .NET 10
- **UI Framework**: Avalonia 11.3
- **Architecture**: MVVM (Model-View-ViewModel)
- **DI Container**: Microsoft.Extensions.DependencyInjection
- **MVVM Toolkit**: CommunityToolkit.Mvvm
- **Windows APIs**: Win32 API via P/Invoke, WinRT

### Key Areas

- **Window Management**: `Services/WindowService.cs` - Handles window enumeration and tracking
- **Dimming Logic**: `Services/DimmingService.cs` - Controls the overlay and dimming behavior
- **Native Interop**: `Native/` - Win32 API declarations and wrappers
- **Settings**: `Models/AppSettings.cs` - Application configuration

## Testing

### Running Tests

```bash
# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test project
dotnet test DeepFocusForWindows.Tests/DeepFocusForWindows.Tests.csproj
```

### Writing Tests

- Place tests in the `DeepFocusForWindows.Tests` project
- Follow the existing test structure (Services/, ViewModels/)
- Use descriptive test names: `MethodName_Scenario_ExpectedBehavior`
- Mock external dependencies (Windows APIs, file system, etc.)

### Test Coverage

Aim for test coverage on:
- Business logic in Services
- ViewModel behavior
- Data model validation
- Critical algorithms

## Code Style

### General Guidelines

- Use **C# 10+** features where appropriate
- Enable **nullable reference types** (`#nullable enable`)
- Follow **Microsoft C# Coding Conventions**
- Use **async/await** for asynchronous operations
- Prefer **LINQ** for collection operations

### Naming Conventions

- **PascalCase**: Classes, methods, properties, events
- **camelCase**: Local variables, parameters
- **_camelCase**: Private fields
- **UPPER_CASE**: Constants

### Code Organization

- Keep files focused and single-responsibility
- Use meaningful names that describe intent
- Document complex algorithms or Win32 API usage
- Use `#region` sparingly, only for P/Invoke declarations

### MVVM Patterns

- ViewModels should not reference Views
- Use `CommunityToolkit.Mvvm` attributes (`[ObservableProperty]`, `[RelayCommand]`)
- Inject services via constructor dependency injection
- Keep UI logic in Views (XAML), business logic in Services

## Pull Request Process

### Before Submitting

1. ✅ Ensure all tests pass
2. ✅ Update documentation if needed
3. ✅ Add tests for new functionality
4. ✅ Rebase on latest `master`
5. ✅ Ensure code follows style guidelines

### Submitting a PR

1. Push your branch to your fork:
   ```bash
   git push origin feature/your-feature-name
   ```

2. Create a Pull Request on GitHub with:
   - Clear title describing the change
   - Description of what changed and why
   - Reference to related issues (e.g., "Fixes #123")
   - Screenshots/GIFs for UI changes

3. Address review feedback:
   ```bash
   # Make changes based on feedback
   git add .
   git commit -m "address review feedback"
   git push origin feature/your-feature-name
   ```

### PR Review Process

- Maintainers will review your PR
- Address feedback and requested changes
- Once approved, a maintainer will merge your PR

## Release Process

### Automated Release Workflow

The project uses GitHub Actions for automated builds and releases. The workflow is defined in `.github/workflows/release.yml`.

### Creating a Release

#### Method 1: Tag-Based Release (Recommended)

```bash
# Create and push a version tag
git tag v1.0.0
git push origin v1.0.0
```

This triggers the workflow automatically and creates a GitHub release with:
- **Self-contained build**: Includes .NET runtime (~100MB), no installation required
- **Framework-dependent build**: Requires .NET 10 runtime (~1MB)

Both builds are:
- Single-file executables (no additional DLLs)
- Zipped and uploaded as release assets
- Versioned according to the tag

#### Method 2: Manual Release

1. Go to **Actions** tab on GitHub
2. Select **"Build and Release"** workflow
3. Click **"Run workflow"**
4. Enter version number (e.g., `1.0.0`)
5. Click **"Run workflow"**

### Release Artifacts

Each release produces two zip files:
- `DeepFocusForWindows-vX.Y.Z-self-contained.zip` - Standalone with .NET runtime
- `DeepFocusForWindows-vX.Y.Z-framework-dependent.zip` - Requires .NET 10 installation

### Version Numbers

Follow [Semantic Versioning](https://semver.org/):
- **MAJOR** (X.0.0): Breaking changes
- **MINOR** (0.X.0): New features (backward-compatible)
- **PATCH** (0.0.X): Bug fixes

### Pre-release Versions

For beta/alpha releases:
```bash
git tag v1.0.0-beta.1
git push origin v1.0.0-beta.1
```

Mark as pre-release in GitHub UI after automatic creation.

## Additional Resources

- [Avalonia Documentation](https://docs.avaloniaui.net/)
- [.NET Documentation](https://docs.microsoft.com/dotnet/)
- [Win32 API Reference](https://docs.microsoft.com/windows/win32/api/)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)

## Questions?

- Open an issue for bug reports or feature requests
- Start a discussion for questions or ideas
- Check existing issues/discussions before creating new ones

Thank you for contributing! 🎉
