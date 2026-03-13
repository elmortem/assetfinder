## WHAT'S NEW:

Addressables Support - Find assets using Addressable references through AssetReference objects, standard and custom reference types, and nested dependencies.

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

- Cleaning up unused assets
- Safe refactoring of complex projects
- Understanding asset and type usage throughout your project
- Tracking down hard-to-find references
- Managing Addressable asset dependencies
- Optimizing project structure

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
- Initial cache building for large projects (~35,000 assets) takes approximately 150 seconds
- Subsequent incremental updates are much faster as they only process changed assets

Asset Scout is suitable for projects of **any genre and size**. Whether you're working on a small indie game or a large-scale commercial project, Asset Scout will help you maintain a clean and efficient asset structure.