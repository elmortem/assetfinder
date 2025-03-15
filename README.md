# Asset Scout for Unity

![color:ff69b4](https://img.shields.io/badge/Unity-2022.3.x-red)

A powerful Unity Editor tool for finding all references to any asset in your project. Whether you're cleaning up unused assets or refactoring your project, Asset Scout helps you track down every usage of materials, prefabs, scriptable objects, and other Unity assets.

## How It Works

Unlike Unity's built-in `GetDependencies` API that only shows direct dependencies (what assets are used by a given asset), Asset Scout solves the reverse dependency problem - finding which assets are using your target asset. The tool not only identifies assets that reference your target asset but also shows the exact property paths where the reference is used.

## Features

- **Reverse Dependencies**: Find which assets reference your target asset (unlike Unity's built-in API)
- **Deep Search**: Finds references in:
  - Scenes
  - Prefabs
  - Scriptable Objects
  - Materials (including shader properties)
  - And other Unity assets
- **Detailed Results**: Shows exact paths to referenced assets
- **Fast & Efficient**: Immediately search with rebuilt cache
- **Performance**: Rebuild cache with progress tracking (approximately 150 seconds for 35,000 assets)
- **Easy to Use**: Simple drag & drop interface
- **Extensible**: Custom processors system for adding your own search logic

<img src="Images/screenshot0.png" width="400">

## Installation

### Option 1: Unity Asset Store (Recommended)
1. Open the Asset Store window in Unity (Window > Asset Store)
2. Search for "Asset Scout"
3. Purchase and download the package
4. Import the package into your project using the Package Manager
5. Follow the on-screen instructions to complete the installation

### Option 2: Unity Package Manager via Git URL
1. Open the Package Manager window in Unity (Window > Package Manager)
2. Click the "+" button in the top-left corner
3. Select "Add package from git URL..."
4. Enter: `https://github.com/elmortem/assetfinder.git?path=AssetFinder/Assets/AssetScout/Package`
5. Click "Add"

### Option 3: Manual Installation
1. Download this repository
2. Copy the `AssetScout` folder from `AssetFinder/Assets/AssetScout/Package` to your Unity project's `Packages` folder

## Usage

1. Open the Asset Scout window:
   - Go to `Tools > Asset Scout`
2. Drag & drop any asset you want to find references to
3. Click "Find References" or wait for automatic search to begin
4. Review the list of found references:
   - Each entry shows the asset containing references
   - Expand entries to see exact paths where the reference is used

## Cache System

Asset Scout uses a cache system to maintain fast search performance:

- **Automatic Updates**: The cache automatically updates when assets are imported, deleted, or moved (if Auto Update Cache is enabled)
- **Manual Controls**: Auto-update can be disabled in the settings if needed
- **Performance**: The cache system is highly optimized for quick reference lookups

## Extensibility

Asset Scout features a powerful processor system that allows you to extend its functionality with custom plugins:

- **Custom Processors**: Create your own reference processors by implementing the `IReferenceProcessor` interface
- **Support for Custom Data Types**: Add support for project-specific asset types or reference systems
- **Special Use Cases**: Handle specialized scenarios such as:
  - Localization keys
  - Weak references to assets
  - Addressable assets
  - Custom asset linking systems
  - Any other project-specific reference patterns
- **UI Integration**: Add custom search fields to the Asset Scout interface
- **Selective Enabling**: Processors can be enabled/disabled individually through the interface

## Requirements

- Unity 2020.3 or later
- .NET Standard 2.0 or later

## License

Free for personal use and indie developers. Commercial licenses available on the Unity Asset Store.
See the [LICENSE](LICENSE) file for full details.

## Support

If you encounter any issues or have questions:
1. Check the [Issues](https://github.com/elmortem/assetfinder/issues) page
2. Create a new issue if your problem isn't already reported
