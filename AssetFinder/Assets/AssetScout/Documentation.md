# Asset Scout Documentation

## Table of Contents
1. Overview
2. Installation
3. Basic Usage
4. How It Works
5. Cache System
6. Interface Elements
7. Search Features
8. Extensibility
9. Technical Requirements
10. Troubleshooting

## 1. Overview
Asset Scout is an editor extension for Unity that helps developers locate all references to any asset in their project. The tool is designed to improve project maintenance by making it easy to track down asset usage across scenes, prefabs, and other Unity assets.

## 2. Installation
The Asset Scout package can be installed in three ways:

### A. Using Unity Asset Store (Recommended):
1. Open the Asset Store window in Unity (Window > Asset Store)
2. Search for "Asset Scout"
3. Purchase and download the package
4. Import the package into your project using the Package Manager
5. Follow the on-screen instructions to complete the installation

### B. Using Unity Package Manager via Git URL:
1. Open the Package Manager window in Unity (Window > Package Manager)
2. Click the "+" button in the top-left corner
3. Select "Add package from git URL..."
4. Enter: `https://github.com/elmortem/assetfinder.git?path=AssetFinder/Assets/AssetScout/Package`
5. Click "Add"

### C. Manual Installation:
1. Download this repository
2. Copy the `AssetScout` folder from `AssetFinder/Assets/AssetScout/Package` to your Unity project's `Packages` folder

**Important**: Please remove old version of Asset Scout before updating!

## 3. Basic Usage
To find references to an asset:
1. Open Asset Scout: Navigate to Tools > Asset Scout in Unity's top menu
2. Select Target Asset:
   - Drag and drop any asset from the Project window
   - Or use the object picker field at the top of the Asset Scout window
3. View Results: The tool will automatically search and display all references if Auto Refresh is enabled, otherwise click "Refresh" button
4. Review the list of found references:
   - Each entry shows the asset containing references
   - Expand entries to see exact paths where the reference is used

## 4. How It Works
Asset Scout solves a fundamental problem that Unity's native API doesn't address - reverse dependencies:

- **Direct vs. Reverse Dependencies**: 
  - Unity's built-in `GetDependencies` API only shows which assets are used by a given asset (direct dependencies)
  - Asset Scout finds which assets are using your target asset (reverse dependencies)

- **Deep Reference Resolution**:
  - Not only identifies which assets reference your target, but also shows the exact property paths where the reference occurs
  - Crawls through complex nested objects to find all reference points

This approach is essential for tasks like refactoring, asset cleanup, and understanding asset usage throughout your project.

## 5. Cache System
Asset Scout uses a cache system to maintain fast search performance:

A. Automatic Updates:
- The cache automatically updates when assets are imported, deleted, or moved (if Auto Update Cache is enabled)
- Changes are tracked and processed in real-time
- Only affected assets are re-processed, ensuring efficient updates

B. Manual Controls:
- Auto-update can be disabled in the settings if needed
- Manual rebuilds are available for complete cache refresh
- Force Rebuild option can be used to regenerate the entire cache

C. Performance:
- The cache system is highly optimized for quick reference lookups
- Initial cache building for large projects (~35,000 assets) takes approximately 150 seconds
- Subsequent incremental updates are much faster as they only process changed assets

## 6. Interface Elements
The Asset Scout window contains:
- Rebuild Button: Updates the asset reference cache
- Force Rebuild Option: Available in the rebuild dropdown menu
- Target Asset Field: For selecting the asset to search for
- Refresh Button: Updates the current search results (visible when Auto Refresh is disabled)
- Results Area: Displays found references with expandable details
- Status Information: Shows last rebuild time and processing status
- Processors: List of available reference processors that can be enabled/disabled

## 7. Search Features
Asset Scout can detect references in:
- Scene files (.unity)
- Prefab assets (.prefab)
- Scriptable Objects
- Materials and their shader properties
- Any other Unity asset types that can contain references

Search results show:
- The asset containing the reference
- Exact property paths where the reference is used
- Hierarchical view of nested references

## 8. Extensibility
Asset Scout features a powerful processor system that allows you to extend its functionality with custom plugins:

A. Custom Processors:
- Create your own reference processors by implementing the `IReferenceProcessor` interface
- Add support for project-specific asset types or reference systems
- Handle special cases like Addressables, localization keys, or custom asset linking systems

B. Support for Custom Data Types:
- Localization keys that reference assets indirectly
- Weak references to assets (non-direct UnityEngine.Object references)
- Addressable assets and asset reference systems
- Custom serialized data that contains asset references
- Any other project-specific reference patterns

C. UI Integration:
- Processors can add custom search fields to the Asset Scout interface
- Each processor can have its own GUI for specialized search parameters
- Processors can be enabled/disabled individually through the interface

D. Implementation Examples:
- Custom asset linking systems where references aren't direct UnityEngine.Object references
- Special handling for scriptable object data fields that contain indirect references
- Project-specific reference patterns that require custom detection logic

## 9. Technical Requirements
- Unity Version: 2020.3 or newer
- .NET Standard: 2.0 or later

## 10. Troubleshooting
Common issues and solutions:

A. Cache Issues:
- If references are outdated: Click the Rebuild button
- For persistent issues: Use Force Rebuild from the dropdown menu

B. Missing References:
- Verify the asset exists in the project
- Check if the asset is actually used in your project
- Rebuild the cache and try again

C. Performance:
- Large projects may require longer initial cache building time
- Regular rebuilds are faster than force rebuilds
- Consider rebuilding cache after major project changes

For additional support or bug reports, please create an issue on the project's GitHub repository.

*Note: This documentation is part of the Asset Scout package. All rights reserved.*