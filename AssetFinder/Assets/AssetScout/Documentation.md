# Asset Scout Documentation

## Table of Contents
1. Overview
2. Installation
3. Basic Usage
4. Cache System
5. Interface Elements
6. Search Features
7. Technical Requirements
8. Troubleshooting

## 1. Overview
Asset Scout is an editor extension for Unity that helps developers locate all references to any asset in their project. The tool is designed to improve project maintenance by making it easy to track down asset usage across scenes, prefabs, and other Unity assets.

## 2. Installation
The Asset Scout package can be installed in two ways:

A. Using Unity Package Manager:
1. Launch Unity and open your project
2. Navigate to Window > Package Manager
3. Click the "+" button in the top-left corner
4. Select "Add package from git URL..."
5. Enter: https://github.com/elmortem/assetfinder.git?path=AssetFinder/Packages/AssetFinder
6. Click "Add"

B. Manual Installation:
1. Download the package files
2. Navigate to your Unity project's Packages folder
3. Copy the AssetFinder folder into the Packages directory

## 3. Basic Usage
To find references to an asset:
1. Open Asset Scout: Navigate to Tools > Asset Scout in Unity's top menu
2. Select Target Asset:
   - Drag and drop any asset from the Project window
   - Or use the object picker field at the top of the Asset Scout window
3. View Results: The tool will automatically search and display all references

## 4. Cache System
Asset Scout uses a cache system to maintain fast search performance:

A. Automatic Updates:
- The cache automatically updates when assets are imported, deleted, or moved
- Changes are tracked and processed in real-time
- Only affected assets are re-processed, ensuring efficient updates

B. Manual Controls:
- Auto-update can be disabled in settings if needed
- Manual rebuilds are available for complete cache refresh

## 5. Interface Elements
The Asset Scout window contains:
- Rebuild Button: Updates the asset reference cache
- Force Rebuild Option: Available in the rebuild dropdown menu
- Target Asset Field: For selecting the asset to search for
- Refresh Button: Updates the current search results
- Results Area: Displays found references with expandable details
- Status Information: Shows last rebuild time and processing status

## 6. Search Features
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

## 7. Technical Requirements
- Unity Version: 2020.3 or newer
- .NET Standard: 2.0 or later
- Dependencies: UniTask package for async operations
- Storage: Requires space for reference cache

## 8. Troubleshooting
Common issues and solutions:

A. Cache Issues:
- If references are outdated: Click the Rebuild button
- For persistent issues: Use Force Rebuild from the dropdown menu

B. Missing References:
- Verify the asset exists in the project
- Check if the asset is actually used
- Rebuild the cache and try again

C. Performance:
- Large projects may require longer initial cache building
- Regular rebuilds are faster than force rebuilds
- Consider rebuilding cache after major project changes

For additional support or bug reports, please create an issue on the project's GitHub repository.

*Note: This documentation is part of the Asset Scout package. All rights reserved. Last updated: January 2025.*