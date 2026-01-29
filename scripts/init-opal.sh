#!/usr/bin/env bash
set -euo pipefail

# OPAL Initialization Script
# Usage: curl -fsSL https://raw.githubusercontent.com/juanmicrosoft/opal/main/scripts/init-opal.sh | bash

VERSION="0.1.0"
REPO_URL="https://github.com/juanmicrosoft/opal"
RAW_URL="https://raw.githubusercontent.com/juanmicrosoft/opal/main"

# Colors (with fallback for non-interactive terminals)
if [[ -t 1 ]]; then
    RED='\033[0;31m'
    GREEN='\033[0;32m'
    YELLOW='\033[1;33m'
    BLUE='\033[0;34m'
    CYAN='\033[0;36m'
    NC='\033[0m' # No Color
else
    RED='' GREEN='' YELLOW='' BLUE='' CYAN='' NC=''
fi

print_banner() {
    echo -e "${CYAN}"
    cat << 'EOF'
   ____  _____       _
  / __ \|  __ \ /\  | |
 | |  | | |__) /  \ | |
 | |  | |  ___/ /\ \| |
 | |__| | |  / ____ \ |____
  \____/|_| /_/    \_\_____|

  Optimized Programming for Agent Logic
EOF
    echo -e "  Version ${VERSION}${NC}"
    echo ""
}

info() { echo -e "${BLUE}[INFO]${NC} $1"; }
success() { echo -e "${GREEN}[OK]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

detect_os() {
    case "$(uname -s)" in
        Darwin*)  echo "macOS" ;;
        Linux*)   echo "Linux" ;;
        MINGW*|MSYS*|CYGWIN*) echo "Windows" ;;
        *)        echo "Unknown" ;;
    esac
}

check_command() {
    if command -v "$1" &> /dev/null; then
        return 0
    else
        return 1
    fi
}

check_dotnet_version() {
    if ! check_command dotnet; then
        return 1
    fi

    local version
    version=$(dotnet --version 2>/dev/null || echo "0.0.0")
    local major
    major=$(echo "$version" | cut -d. -f1)

    if [[ "$major" -ge 8 ]]; then
        return 0
    else
        return 1
    fi
}

check_prerequisites() {
    info "Checking prerequisites..."

    local os
    os=$(detect_os)
    success "Detected OS: $os"

    # Check .NET SDK
    if check_dotnet_version; then
        local version
        version=$(dotnet --version)
        success ".NET SDK $version installed"
    else
        error ".NET SDK 8.0+ is required. Install from https://dotnet.microsoft.com/download"
    fi

    # Check git
    if check_command git; then
        success "git installed"
    else
        warn "git not found - some features may not work"
    fi

    echo ""
}

install_opalc() {
    info "Installing opalc global tool..."

    # Check if already installed
    if dotnet tool list -g | grep -q "^opalc "; then
        warn "opalc is already installed. Updating..."
        dotnet tool update -g opalc 2>/dev/null || {
            info "Tool not yet published to NuGet. Building from source..."
            install_from_source
            return
        }
    else
        dotnet tool install -g opalc 2>/dev/null || {
            info "Tool not yet published to NuGet. Building from source..."
            install_from_source
            return
        }
    fi

    success "opalc installed successfully"
}

install_from_source() {
    local temp_dir
    temp_dir=$(mktemp -d)
    local clone_success=false

    info "Cloning OPAL repository..."
    if git clone --depth 1 "$REPO_URL" "$temp_dir" 2>/dev/null; then
        clone_success=true
    else
        warn "Could not clone from GitHub. This is expected if the repo is not public yet."
        warn "You can manually install opalc by running:"
        echo "  git clone $REPO_URL"
        echo "  cd opal"
        echo "  dotnet pack src/Opal.Compiler/Opal.Compiler.csproj -c Release -o ./nupkg"
        echo "  dotnet tool install -g --add-source ./nupkg opalc"
        rm -rf "$temp_dir"
        return
    fi

    info "Building opalc..."
    cd "$temp_dir"
    dotnet pack src/Opal.Compiler/Opal.Compiler.csproj -c Release -o ./nupkg || {
        cd -
        rm -rf "$temp_dir"
        error "Build failed"
    }

    info "Installing from local package..."
    dotnet tool install -g --add-source ./nupkg opalc 2>/dev/null || dotnet tool update -g --add-source ./nupkg opalc 2>/dev/null || true
    cd - > /dev/null

    rm -rf "$temp_dir"
    success "opalc installed from source"
}

setup_claude_skills() {
    info "Setting up Claude Code skills..."

    # Create .claude/skills directory
    mkdir -p .claude/skills

    # Download skill files
    if check_command curl; then
        curl -fsSL "${RAW_URL}/.claude/skills/opal.md" -o .claude/skills/opal.md || warn "Could not download opal.md"
        curl -fsSL "${RAW_URL}/.claude/skills/opal-convert.md" -o .claude/skills/opal-convert.md || warn "Could not download opal-convert.md"
    elif check_command wget; then
        wget -q "${RAW_URL}/.claude/skills/opal.md" -O .claude/skills/opal.md || warn "Could not download opal.md"
        wget -q "${RAW_URL}/.claude/skills/opal-convert.md" -O .claude/skills/opal-convert.md || warn "Could not download opal-convert.md"
    else
        warn "Neither curl nor wget found. Skipping skill download."
        return
    fi

    success "Claude Code skills installed in .claude/skills/"
}

create_sample_project() {
    if [[ -f "*.opal" ]] || [[ -d "samples" ]]; then
        info "Project files already exist. Skipping sample creation."
        return
    fi

    info "Creating sample OPAL project..."

    mkdir -p src

    # Create hello.opal
    cat > src/hello.opal << 'OPAL'
§M[m001:Hello]
§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §P "Hello from OPAL!"
§/F[f001]
§/M[m001]
OPAL

    # Create project file
    cat > src/Hello.csproj << 'CSPROJ'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
CSPROJ

    success "Sample project created in src/"
    echo ""
    info "To compile and run:"
    echo "  opalc --input src/hello.opal --output src/hello.g.cs"
    echo "  dotnet run --project src/Hello.csproj"
}

print_next_steps() {
    echo ""
    echo -e "${GREEN}=== Setup Complete ===${NC}"
    echo ""
    echo "Next steps:"
    echo "  1. Run 'opalc --help' to see compiler options"
    echo "  2. Use '/opal' in Claude Code to write OPAL code"
    echo "  3. Use '/opal-convert' to convert C# to OPAL"
    echo ""
    echo "Documentation: ${REPO_URL}"
    echo ""
}

main() {
    print_banner
    check_prerequisites
    install_opalc
    setup_claude_skills
    create_sample_project
    print_next_steps
}

main "$@"
