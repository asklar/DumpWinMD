﻿using MiddleweightReflection;
using System;
using System.Reflection.Metadata;
using System.Linq;
using System.Collections.Generic;

using CustomAttributes = System.Collections.Immutable.ImmutableArray<MiddleweightReflection.MrCustomAttribute>;
using System.Xml;
using System.IO;
using System.Diagnostics;
using System.Collections.Immutable;

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
                            Debug.WriteLine($"EXPERIMENTAL {t}");
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

        XmlWriter writer;

        void DumpTypes()
        {
            MemoryStream ms = new MemoryStream();
            writer = XmlWriter.Create(ms, new XmlWriterSettings() { Indent = true });
            writer.WriteStartDocument();
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
            var types = assembly.GetAllTypes().Skip(1);

            var namespaces = assembly.GetAllTypes().Skip(1).Select(x => x.GetNamespace()).Distinct();
            writer.WriteStartElement("assembly");
            writer.WriteAttributeString("namespace", namespaces.FirstOrDefault());

            foreach (var t in types)
            {
                if (IsExclusiveInterface(t))
                {
                    continue;
                }
                writer.WriteStartElement(GetTypeKind(t));
                writer.WriteAttributeString("name", t.GetName());
                if (!t.IsInterface && t.IsAbstract)
                {
                    writer.WriteAttributeString("abstract", "true");
                }
                var attrs = GetCustomAttrs(t);
                WriteAttrs(attrs);

                Debug.WriteLine($"{GetTypeKind(t)} {t.GetName()} {attrs.GetValueOrDefault("docstring")} {attrs.GetValueOrDefault("docdefault")}");
                var kind = GetTypeKind(t);

                var baseTypesToSkip = new string[]
                {
                    typeof(MulticastDelegate).FullName,
                    typeof(ValueType).FullName,
                    typeof(Enum).FullName,
                    typeof(object).FullName,
                    typeof(Attribute).FullName,
                };

                if (t.GetBaseType() != null && !baseTypesToSkip.Contains(t.GetBaseType().GetFullName()))
                {
                    writer.WriteElementString("baseType", t.GetBaseType().GetFullName());
                }
                var ifaces = t.GetInterfaces().Where(x => !IsExclusiveInterface(x));
                if (ifaces.Count() > 0)
                {
                    writer.WriteStartElement("implements");
                    foreach (var i in ifaces)
                    {
                        writer.WriteElementString("interface", i.GetFullName());
                    }
                    writer.WriteEndElement();
                }


                foreach (var m in t.GetProperties())
                {
                    var mattrs = GetCustomAttrs(m);
                    var mattrsGetter = GetCustomAttrs(m.Getter);
                    foreach (var kv in mattrsGetter) { mattrs[kv.Key] = kv.Value; }
                    writer.WriteStartElement("member");
                    writer.WriteAttributeString("name", m.GetName());
                    writer.WriteAttributeString("kind", "property");
                    bool isStatic = (m.Getter.MethodDefinition.Attributes & System.Reflection.MethodAttributes.Static) == System.Reflection.MethodAttributes.Static;
                    if (isStatic) writer.WriteAttributeString("static", isStatic.ToString().ToLower());
                    bool isReadonly = m.Setter == null || !m.Setter.GetParsedMethodAttributes().IsPublic;
                    if (isReadonly) writer.WriteAttributeString("isReadonly", isReadonly.ToString().ToLower());
                    WriteAttrs(mattrs);
                    WriteType(m.GetPropertyType());

                    writer.WriteEndElement();
                    Debug.WriteLine($"  prop {m.GetPropertyType().GetPrettyFullName()} {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");

                }

                t.GetMethodsAndConstructors(out var methods, out var ctors);
                if (GetTypeKind(t) != "delegate")
                {
                    foreach (var m in methods)
                    {
                        var mattrs = GetCustomAttrs(m);
                        writer.WriteStartElement("member");
                        writer.WriteAttributeString("name", m.GetName());
                        writer.WriteAttributeString("kind", "method");
                        bool isStatic = m.MethodDefinition.Attributes.HasFlag(System.Reflection.MethodAttributes.Static);
                        if (isStatic) writer.WriteAttributeString("static", isStatic.ToString().ToLower());
                        bool isAbstract = !t.IsInterface && m.MethodDefinition.Attributes.HasFlag(System.Reflection.MethodAttributes.Abstract);
                        if (isAbstract) writer.WriteAttributeString("abstract", isAbstract.ToString().ToLower());

                        WriteAttrs(mattrs);
                        WriteMethodSignature(m);
                        writer.WriteEndElement();
                        Debug.WriteLine($"  method {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
                    }

                    foreach (var m in ctors)
                    {
                        var mattrs = GetCustomAttrs(m);
                        writer.WriteStartElement("member");
                        writer.WriteAttributeString("name", m.DeclaringType.GetName());
                        writer.WriteAttributeString("kind", "ctor");

                        WriteAttrs(mattrs);
                        WriteMethodSignature(m, false);
                        writer.WriteEndElement();
                        Debug.WriteLine($"  ctor {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
                    }
                }
                else
                {
                    var m = t.GetInvokeMethod();
                    var mattrs = GetCustomAttrs(m);

                    WriteAttrs(mattrs);
                    WriteMethodSignature(m);
                }

                foreach (var m in t.GetFields().Where(x => !x.IsSpecialName))
                {
                    var mattrs = GetCustomAttrs(m);
                    writer.WriteStartElement("member");
                    writer.WriteAttributeString("name", m.GetName());
                    writer.WriteAttributeString("kind", "field");

                    WriteAttrs(mattrs);
                    writer.WriteEndElement();
                    Debug.WriteLine($"  field {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
                }

                foreach (var m in t.GetEvents())
                {
                    var mattrs = GetCustomAttrs(m);
                    writer.WriteStartElement("member");
                    writer.WriteAttributeString("name", m.GetName());
                    writer.WriteAttributeString("kind", "event");

                    WriteAttrs(mattrs);
                    writer.WriteEndElement();
                    Debug.WriteLine($"  event {m.GetName()} {mattrs.GetValueOrDefault("docstring")} {mattrs.GetValueOrDefault("docdefault")}");
                }

                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndDocument();
            writer.Flush();
            writer.Close();
            ms.Position = 0;
            using (TextReader tr = new StreamReader(ms))
            {
                var xml = tr.ReadToEnd();
            }
        }

        private static string MapManagedTypeToWinRtType(MrType t)
        {
            var map = new Dictionary<string, string> {
                { typeof(Object).FullName, "Windows.Foundation.IInspectable" },
                { "System.Collections.Generic.IList`1",  "Windows.Foundation.Collections.IVector`1" },
                { "System.Collections.Generic.IReadOnlyList`1", "Windows.Foundation.Collections.IVectorView`1" },
                { "System.Collections.Generic.IDictionary`2", "Windows.Foundation.Collections.IMap`2" },
                { "System.Collections.Generic.IReadOnlyDictionary`2", "Windows.Foundation.Collections.IMapView`2" },
            };

            if (map.ContainsKey(t.GetFullName()))
            {
                return map[t.GetFullName()];
            }
            if (t.GetNamespace() == "System")
            {
                return t.GetName();
            }
            else
            {
                return t.GetFullName();
            }
        }

        private void WriteType(MrType t)
        {
            var name = "";

            var gargs = t.GetGenericArguments();
            var gparams = t.GetGenericTypeParameters();
            name = GetSimpleTypeName(t, gargs, gparams);

            writer.WriteStartElement("type");
            if (t.IsArray)
            {
                writer.WriteAttributeString("array", "true");
                name = name.Replace("[]", "");
            }
            if (!gargs.IsEmpty)
            {
                writer.WriteStartElement("generic");
                writer.WriteAttributeString("type", name);
                writer.WriteStartElement("params");
                foreach (var p in gparams)
                {
                    WriteType(p);
                }
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
            else
            {
                writer.WriteString(name);
            }
            writer.WriteEndElement();
        }

        private static string GetSimpleTypeName(MrType t, ImmutableArray<MrType> gargs, ImmutableArray<MrType> gparams)
        {
            string name;
            if (t.IsTypeCode)
            {
                name = t.TypeCode.ToString();
            }
            else if (t.GetNamespace() == "System")
            {
                name = MapManagedTypeToWinRtType(t);
            }
            else if (!gargs.IsEmpty || !gparams.IsEmpty)
            {
                name = MapManagedTypeToWinRtType(t);
                name = name.Substring(0, name.IndexOf('`'));

            }
            else
            {
                name = t.GetFullName();
            }

            return name;
        }

        private static bool IsExclusiveInterface(MrType t)
        {
            return t.IsInterface && t.GetCustomAttributes().Any(IsExclusiveToAttribute);
        }

        private static bool IsExclusiveToAttribute(MrCustomAttribute ca)
        {
            ca.GetNameAndNamespace(out var name, out var ns);
            return name == "ExclusiveToAttribute";
        }

        private void WriteMethodSignature(MrMethod m, bool returnType = true)
        {
            if (returnType) WriteType(m.MethodSignature.ReturnType);

            writer.WriteStartElement("params");
            int i = 0;
            foreach (var p in m.GetParameters())
            {
                writer.WriteStartElement("param");
                writer.WriteAttributeString("name", p.GetParameterName());
                writer.WriteAttributeString("index", i.ToString());
                WriteType(p.GetParameterType());

                i++;
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        private void WriteAttrs(Dictionary<string, object> attrs)
        {
            if (attrs.ContainsKey("experimental")) writer.WriteAttributeString("experimental", attrs["experimental"].ToString().ToLower());

            if (attrs.ContainsKey("docstring")) writer.WriteElementString("summary", attrs["docstring"] as string);
            if (attrs.ContainsKey("docdefault")) writer.WriteElementString("default", attrs["docdefault"] as string);
            if (attrs.ContainsKey("deprecated")) writer.WriteElementString("deprecated", attrs["deprecated"] as string);
        }

        static void Main(string[] args)
        {
            new Program().DumpTypes();
        }
        
    }
}
