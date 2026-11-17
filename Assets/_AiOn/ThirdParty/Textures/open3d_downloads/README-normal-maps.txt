WoodFloor050_NormalDX.jpg is DirectX-style (Y-, green channel pointing down), as its name says.
Unity expects OpenGL-style (Y+) normal maps: set Texture Type = Normal map and tick "Flip Green Channel"
in the import settings (TextureImporter.flipGreenChannel = true).
The three images were extracted unchanged from WoodFloorTexture.zip (release 20220301-data).
