## WHAT'S NEW:

**Major Performance Improvements** - This update focuses entirely on speed and efficiency. Incremental rebuilds are significantly faster, memory consumption is reduced, and cache operations (save/load) are optimized. If you're working with large projects, this update is for you

**Asset Scout** solves a fundamental problem that Unity's native API doesn't address - reverse dependencies. While Unity's built-in GetDependencies API only shows what assets are used by a given asset (direct dependencies), Asset Scout finds which assets are using your target asset (**reverse dependencies**).

## KEY FEATURES

- **Reverse Dependency Tracking** - Find all assets that reference your target asset
- **Type Reference Search** - Discover which assets use specific C# types, including SerializeReference fields and components
- **Addressables Reference Search** - Locate all assets that reference your Addressable resources
- **Deep Search** - Detect references in scenes, prefabs, scriptable objects, materials, and more
- **Detailed Results** - See exact property paths where references are used
- **Fast Performance** - Optimized cache system for quick searches
- **Easy to Use** - Simple drag & drop interface with clear results
- **Extensible** - Create custom processors for specialized search needs

## PERFECT FOR

- **Game Designers** - Understand how configs and data assets are connected across the project
- **Technical Artists** - Find which sprites, materials, and meshes are used in which prefabs - and which can be safely removed
- **Indie Developers & GameJam Teams** - Quickly navigate and understand a rapidly evolving project
- **Anyone Refactoring** - Safely rename, move, or delete assets knowing exactly what depends on them

## ADVANCED FEATURES

- Cache System - Automatically updates when assets change
- Custom Processors - Extend functionality with your own search logic
- Addressables Integration - Find references through the Addressables system
- Support for Custom Data Types - Handle localization keys, weak references
- UI Integration - Add custom search fields to the interface
- Selective Enabling - Enable/disable processors individually

## TECHNICAL DETAILS

- Supports Unity 2022.3 or later
- Requires .NET Standard 2.0 or later
- Initial cache building for large projects (~70,000 searchable assets) takes approximately 160 seconds
- Small indie projects (~300 searchable assets) build in under 3 seconds
- Subsequent incremental updates are much faster as they only process changed assets

**Asset Scout** is suitable for projects of **any genre and size**. Whether you're working on a small indie game or a 
large-scale commercial project, Asset Scout will help you maintain a clean and efficient asset structure.