# DumpWinMD
Generate an XML representation of a Windows Metadata file

## Usage:
`DumpWinMD myAssembly.winmd`

Produces a file `myAssembly.xml` with an XML description of the API surface (types, members, attributes, etc.)



## Building
This project uses [Middleweight reflection](https://github.com/MikeHillberg/middleweight-reflection). Currently it needs some changes that haven't been made it to the trunk so it needs https://github.com/asklar/middleweight-reflection/tree/traits. Once these changes make it into the trunk, I'll update this project to use the updated NuGet package.
