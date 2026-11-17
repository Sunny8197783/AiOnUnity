The *_Normal.jpg files in this folder are DirectX-style (Y-, green channel pointing down).
They are the original 2021 cc0textures.com / ambientCG downloads, kept byte-identical to the source repository.
Unity expects OpenGL-style (Y+) normal maps: set Texture Type = Normal map and tick "Flip Green Channel"
in the import settings (TextureImporter.flipGreenChannel = true) for these three files.
Checked 2026-11-16 by correlating the green channel with the gradient of the matching height maps.
