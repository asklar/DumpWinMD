using MiddleweightReflection;
using System;
using System.Reflection.Metadata;
using System.Linq;
using System.Collections.Generic;

using CustomAttributes = System.Collections.Immutable.ImmutableArray<MiddleweightReflection.MrCustomAttribute>;

namespace DumpWinMD
{
    class StringCustomAttributeProvider : ICustomAttributeTypeProvider<string>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            if (typeCode == PrimitiveTypeCode.String)
            {
                return "string";
            }
            throw new Exception();
        }

        public string GetSystemType()
        {
            return "System.String";
        }

        public string GetSZArrayType(string elementType)
        {
            throw new NotImplementedException();
        }

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            throw new NotImplementedException();
        }

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            
            throw new NotImplementedException();
        }

        public string GetTypeFromSerializedName(string name)
        {
            throw new NotImplementedException();
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(string type)
        {
            throw new NotImplementedException();
        }

        public bool IsSystemType(string type)
        {
            throw new NotImplementedException();
        }
    }
    class Program
    {
        string GetContentFromAttribute(CustomAttribute val)
        {
            var args = val.DecodeValue(new StringCustomAttributeProvider());
            var content = args.NamedArguments.Where(x => x.Name == "Content").First();
            return content.Value as string;
        }

        Dictionary<string, object> GetCustomAttrs(IHasCustomAttributes t)
        {
            var map = new Dictionary<string, object>();
            foreach (var a in t.GetCustomAttributes())
            {
                a.GetNameAndNamespace(out string name, out string ns);
                switch (name)
                {
                    case "DocDefaultAttribute":
                    case "DocStringAttribute":
                        {
                            var val = a.CustomAttribute;
                            map[name.Substring(0, name.IndexOf("Attribute")).ToLower()] = GetContentFromAttribute(val);
                            break;
                        }
                    case "ExperimentalAttribute":
                        {
                            Console.WriteLine($"EXPERIMENTAL {t}");
                            map["experimental"] = true; break;
                        }
                    case "DeprecatedAttribute":
                        {
                            var val = a.CustomAttribute;
                            a.GetArguments(out var fixedArgs, out var namedArgs);//, windows_winmd);
                            map["deprecated"] = fixedArgs[0].Value;
                            break;
                        }
                }
            }
            return map;
        }
        MrAssembly windows_winmd;

        string GetTypeKind(MrType t)
        {
            if (t.IsClass && t.GetInvokeMethod() != null && t.GetBaseType().GetName() == "MulticastDelegate") return "delegate";
            if (t.IsClass) return "class";
            else if (t.IsInterface) return "interface";
            else if (t.IsEnum) return "enum";
            else if (t.IsStruct) return "struct";
            throw new ArgumentException();
        }
        void DumpTypes()
        {
            var context = new MrLoadContext(true);
            context.FakeTypeRequired += (sender, e) => {
                var ctx = sender as MrLoadContext;
                if (e.AssemblyName == "Windows.Foundation.FoundationContract")
                {
                    e.ReplacementType = ctx.GetTypeFromAssembly(e.TypeName, "Windows");
                }
            };
            windows_winmd = context.LoadAssemblyFromPath(@"C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.19041.0\Windows.winmd");//, "Windows.winmd");
            var assembly = context.LoadAssemblyFromPath(@"C:\rnw\vnext\target\x86\Debug\Microsoft.ReactNative\Microsoft.ReactNative.winmd");
            context.FinishLoading();
            var types = assembly.GetAllTypes();
            
            foreach (var t in types)
            {
                if (t.IsInterface && t.GetCustomAttributes().Any(ca => { ca.GetNameAndNamespace(out var name, out var ns); return name == "ExclusiveToAttribute"; }))
                {
                    continue;
                }
                var attrs = GetCustomAttrs(t);
                Console.WriteLine($"{GetTypeKind(t)} {t.GetName()} {attrs.GetValueOrDefault("docstring")} {attrs.GetValueOrDefault("docdefault")}");
                
                foreach (var p in t.GetProperties())
                {
                    var pattrs = GetCustomAttrs(p);
                    Console.WriteLine($"  prop {p.GetPropertyType().GetPrettyFullName()} {p.GetName()} {pattrs.GetValueOrDefault("docstring")} {pattrs.GetValueOrDefault("docdefault")}");
                }

                t.GetMethodsAndConstructors(out var methods, out var ctors);
                foreach (var m in methods)
                {
                    var mattrs = GetCustomAttrs(m);
                    Console.WriteLine($"  method {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
                }

                foreach (var m in ctors)
                {
                    var mattrs = GetCustomAttrs(m);
                    Console.WriteLine($"  ctor {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
                }

                foreach (var f in t.GetFields())
                {
                    if (!f.IsSpecialName)
                    {
                        var fattrs = GetCustomAttrs(f);
                        Console.WriteLine($"  field {f.GetName()} {fattrs.GetValueOrDefault("docstring")} {fattrs.GetValueOrDefault("docdefault")}");
                    }
                }

                foreach (var f in t.GetEvents())
                {
                    var fattrs = GetCustomAttrs(f);
                    Console.WriteLine($"  event {f.GetName()} {fattrs.GetValueOrDefault("docstring")} {fattrs.GetValueOrDefault("docdefault")}");
                }
            }
        }


        static void Main(string[] args)
        {
            new Program().DumpTypes();
        }
        
    }
}
