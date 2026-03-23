using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;
#if PACKAGE_TIMELINE
using UnityEngine.Timeline;
using UnityEngine.Playables;
#endif

namespace AssetScout.Crawlers
{
	public class CustomCrawler : IAssetCrawler
	{
		public static bool IsSupported(object currentObject)
		{
			return currentObject is Material
				|| currentObject is SpriteAtlas
				|| currentObject is AnimatorController
				|| currentObject is AnimatorOverrideController
				|| currentObject is AnimationClip
				|| currentObject is SpriteRenderer
				|| currentObject is ParticleSystem
				|| currentObject is ParticleSystemRenderer
				|| currentObject is Renderer
#if PACKAGE_TIMELINE
				|| currentObject is TimelineAsset
#endif
				;
		}

		public bool CanCrawl(object currentObject) => IsSupported(currentObject);

		public IEnumerable<TraversalContext> GetChildren(object currentObject, TraversalContext parentContext)
		{
			if (currentObject is Material material && material != null)
				return ProcessMaterial(material, parentContext);

			if (currentObject is SpriteAtlas atlas && atlas != null)
				return ProcessSpriteAtlas(atlas, parentContext);

			if (currentObject is AnimatorOverrideController overrideController && overrideController != null)
				return ProcessAnimatorOverrideController(overrideController, parentContext);

			if (currentObject is AnimatorController controller && controller != null)
				return ProcessAnimatorController(controller, parentContext);

			if (currentObject is AnimationClip clip && clip != null)
				return ProcessAnimationClip(clip, parentContext);

			if (currentObject is SpriteRenderer spriteRenderer && spriteRenderer != null)
				return ProcessSpriteRenderer(spriteRenderer, parentContext);

			if (currentObject is ParticleSystem particleSystem && particleSystem != null)
				return ProcessParticleSystem(particleSystem, parentContext);

			if (currentObject is ParticleSystemRenderer psRenderer && psRenderer != null)
				return ProcessParticleSystemRenderer(psRenderer, parentContext);

			if (currentObject is Renderer renderer && renderer != null)
				return ProcessRenderer(renderer, parentContext);

#if PACKAGE_TIMELINE
			if (currentObject is TimelineAsset timeline && timeline != null)
				return ProcessTimeline(timeline, parentContext);
#endif

			return System.Array.Empty<TraversalContext>();
		}

		// ── Material ──

		private static IEnumerable<TraversalContext> ProcessMaterial(Material material, TraversalContext parentContext)
		{
			var shader = material.shader;
			if (shader != null)
			{
				yield return parentContext.CreateChildContext(shader,
					$"{parentContext.CurrentPath}.shader");
			}

			var propertyCount = ShaderUtil.GetPropertyCount(material.shader);
			for (int i = 0; i < propertyCount; i++)
			{
				if (ShaderUtil.GetPropertyType(material.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
				{
					var propertyName = ShaderUtil.GetPropertyName(material.shader, i);
					var texture = material.GetTexture(propertyName);

					if (texture != null)
					{
						yield return parentContext.CreateChildContext(texture,
							$"{parentContext.CurrentPath}.{propertyName}");
					}
				}
			}
		}

		// ── SpriteAtlas ──

		private static IEnumerable<TraversalContext> ProcessSpriteAtlas(SpriteAtlas atlas,
			TraversalContext parentContext)
		{
			var packables = atlas.GetPackables();
			for (int i = 0; i < packables.Length; i++)
			{
				if (packables[i] != null)
				{
					yield return parentContext.CreateChildContext(packables[i],
						$"{parentContext.CurrentPath}.Packables[{i}]");
				}
			}

			if (atlas.spriteCount > 0)
			{
				var sprites = new Sprite[atlas.spriteCount];
				atlas.GetSprites(sprites);
				for (int i = 0; i < sprites.Length; i++)
				{
					if (sprites[i] != null)
					{
						yield return parentContext.CreateChildContext(sprites[i],
							$"{parentContext.CurrentPath}.PackedSprites[{i}]");
					}
				}
			}
		}

		// ── Animator ──

		private static IEnumerable<TraversalContext> ProcessAnimatorController(AnimatorController controller,
			TraversalContext parentContext)
		{
			var clips = controller.animationClips;
			for (int i = 0; i < clips.Length; i++)
			{
				if (clips[i] != null)
				{
					yield return parentContext.CreateChildContext(clips[i],
						$"{parentContext.CurrentPath}.AnimationClips[{i}]");
				}
			}
		}

		private static IEnumerable<TraversalContext> ProcessAnimatorOverrideController(
			AnimatorOverrideController overrideController, TraversalContext parentContext)
		{
			var baseController = overrideController.runtimeAnimatorController;
			if (baseController != null)
			{
				yield return parentContext.CreateChildContext(baseController,
					$"{parentContext.CurrentPath}.RuntimeAnimatorController");
			}

			var overrides =
				new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideController.overridesCount);
			overrideController.GetOverrides(overrides);

			for (int i = 0; i < overrides.Count; i++)
			{
				if (overrides[i].Value != null)
				{
					yield return parentContext.CreateChildContext(overrides[i].Value,
						$"{parentContext.CurrentPath}.Overrides[{i}]");
				}
			}
		}

		private static IEnumerable<TraversalContext> ProcessAnimationClip(AnimationClip clip,
			TraversalContext parentContext)
		{
			var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
			foreach (var binding in bindings)
			{
				var keyframes = AnimationUtility.GetObjectReferenceCurve(clip, binding);
				for (int i = 0; i < keyframes.Length; i++)
				{
					if (keyframes[i].value != null)
					{
						yield return parentContext.CreateChildContext(keyframes[i].value,
							$"{parentContext.CurrentPath}.{binding.propertyName}[{i}]");
					}
				}
			}
		}

		// ── Renderer / SpriteRenderer / ParticleSystem ──

		private static IEnumerable<TraversalContext> ProcessRenderer(Renderer renderer, TraversalContext parentContext)
		{
			var materials = renderer.sharedMaterials;
			for (int i = 0; i < materials.Length; i++)
			{
				if (materials[i] != null)
				{
					yield return parentContext.CreateChildContext(materials[i],
						$"{parentContext.CurrentPath}.Materials[{i}]");
				}
			}
		}

		private static IEnumerable<TraversalContext> ProcessSpriteRenderer(SpriteRenderer spriteRenderer,
			TraversalContext parentContext)
		{
			if (spriteRenderer.sprite != null)
			{
				yield return parentContext.CreateChildContext(spriteRenderer.sprite,
					$"{parentContext.CurrentPath}.Sprite");
			}

			foreach (var child in ProcessRenderer(spriteRenderer, parentContext))
				yield return child;
		}

		private static IEnumerable<TraversalContext> ProcessParticleSystem(ParticleSystem ps,
			TraversalContext parentContext)
		{
			// Texture Sheet Animation — sprites
			var textureSheet = ps.textureSheetAnimation;
			if (textureSheet.enabled && textureSheet.mode == ParticleSystemAnimationMode.Sprites)
			{
				for (int i = 0; i < textureSheet.spriteCount; i++)
				{
					var sprite = textureSheet.GetSprite(i);
					if (sprite != null)
					{
						yield return parentContext.CreateChildContext(sprite,
							$"{parentContext.CurrentPath}.TextureSheetAnimation.Sprites[{i}]");
					}
				}
			}

			// Sub Emitters — ParticleSystem references (can be prefabs)
			var subEmitters = ps.subEmitters;
			if (subEmitters.enabled)
			{
				for (int i = 0; i < subEmitters.subEmittersCount; i++)
				{
					var subSystem = subEmitters.GetSubEmitterSystem(i);
					if (subSystem != null)
					{
						yield return parentContext.CreateChildContext(subSystem.gameObject,
							$"{parentContext.CurrentPath}.SubEmitters[{i}]");
					}
				}
			}
		}

		private static IEnumerable<TraversalContext> ProcessParticleSystemRenderer(
			ParticleSystemRenderer psRenderer, TraversalContext parentContext)
		{
			// Mesh particles
			var meshes = new Mesh[psRenderer.meshCount];
			psRenderer.GetMeshes(meshes);
			for (int i = 0; i < meshes.Length; i++)
			{
				if (meshes[i] != null)
				{
					yield return parentContext.CreateChildContext(meshes[i],
						$"{parentContext.CurrentPath}.Meshes[{i}]");
				}
			}

			// Trail material
			if (psRenderer.trailMaterial != null)
			{
				yield return parentContext.CreateChildContext(psRenderer.trailMaterial,
					$"{parentContext.CurrentPath}.TrailMaterial");
			}

			// Base renderer materials
			foreach (var child in ProcessRenderer(psRenderer, parentContext))
				yield return child;
		}

		// ── Timeline ──

#if PACKAGE_TIMELINE
		private static IEnumerable<TraversalContext> ProcessTimeline(TimelineAsset timeline,
			TraversalContext parentContext)
		{
			foreach (var track in timeline.GetOutputTracks())
			{
				if (track == null)
					continue;

				var trackPath = $"{parentContext.CurrentPath}/{track.name}";

				foreach (var clip in track.GetClips())
				{
					var clipAsset = clip.asset;
					if (clipAsset == null)
						continue;

					var clipPath = $"{trackPath}/{clip.displayName}";

					if (clipAsset is AnimationPlayableAsset animAsset)
					{
						if (animAsset.clip != null)
						{
							yield return parentContext.CreateChildContext(animAsset.clip,
								$"{clipPath}.AnimationClip");
						}
					}
					else if (clipAsset is AudioPlayableAsset audioAsset)
					{
						if (audioAsset.clip != null)
						{
							yield return parentContext.CreateChildContext(audioAsset.clip,
								$"{clipPath}.AudioClip");
						}
					}
					else
					{
						// ControlPlayableAsset, custom PlayableAssets, etc.
						yield return parentContext.CreateChildContext(clipAsset,
							$"{clipPath}.Asset");
					}
				}
			}
		}
#endif
	}
}
