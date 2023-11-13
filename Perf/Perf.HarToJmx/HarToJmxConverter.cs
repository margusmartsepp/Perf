using System.IO.Abstractions;
using System.Text.Json;
using System.Xml.Linq;

namespace Perf.HarToJmx
{
	public interface IXDocumentWrapper
	{
		XDocument Load(string uri);
		void Save(string fileName);
		// other XDocument methods...
	}
	public class HarToJmxConverter
	{
		private readonly IFileSystem _fileSystem;
		private readonly IXDocumentWrapper _xDocumentWrapper;

		public HarToJmxConverter(IFileSystem fileSystem, IXDocumentWrapper xDocumentWrapper)
		{
			_fileSystem = fileSystem;
			_xDocumentWrapper = xDocumentWrapper;
		}

		public void Convert(string harFilePath, string jmxOutputPath)
		{
			var harJson = _fileSystem.File.ReadAllText(harFilePath);
			var har = JsonSerializer.Deserialize<JsonElement?>(harJson);

			if (har != null && har.Value.TryGetProperty("log", out var log) && log.TryGetProperty("entries", out var entries))
			{
				var jmxDocument = CreateJmxDocument();

				foreach (var entry in entries.EnumerateArray())
				{
					var url = GetEntryProperty(entry, "request.url");
					var method = GetEntryProperty(entry, "request.method");
					var postData = GetEntryProperty(entry, "request.postData.text");
					var headers = GetEntryHeaders(entry);

					var sampler = CreateHttpSampler(url, method, postData, headers);
					AddSamplerToJmxDocument(jmxDocument, sampler);
				}

				// jmxDocument.Save(jmxOutputPath);
				_xDocumentWrapper.Save(jmxOutputPath);
			}
		}

		private static XDocument CreateJmxDocument()
		{
			return new XDocument(
					new XElement("jmeterTestPlan",
							new XAttribute("version", "1.2"),
							new XAttribute("properties", "2.9"),
							new XAttribute("jmeter", "3.1 r1770033"),
							new XElement("hashTree",
									new XElement("TestPlan",
											new XAttribute("guiclass", "TestPlanGui"),
											new XAttribute("testclass", "TestPlan"),
											new XAttribute("testname", "Test Plan"),
											new XAttribute("enabled", "true"),
											new XElement("stringProp", new XAttribute("name", "TestPlan.comments"), ""),
											new XElement("boolProp", new XAttribute("name", "TestPlan.functional_mode"), "false"),
											new XElement("boolProp", new XAttribute("name", "TestPlan.serialize_threadgroups"), "false"),
											new XElement("elementProp",
													new XAttribute("name", "TestPlan.user_defined_variables"),
													new XAttribute("elementType", "Arguments"),
													new XAttribute("guiclass", "ArgumentsPanel"),
													new XAttribute("testclass", "Arguments"),
													new XAttribute("testname", "User Defined Variables"),
													new XAttribute("enabled", "true"),
													new XElement("collectionProp", new XAttribute("name", "Arguments.arguments"))
											),
											new XElement("stringProp", new XAttribute("name", "TestPlan.user_define_classpath"), "")
									),
									new XElement("hashTree")
							)
					)
			);
		}

		private static string GetEntryProperty(JsonElement entry, string propertyPath)
		{
			var propertyNames = propertyPath.Split('.');
			var currentObject = entry;

			foreach (var propertyName in propertyNames)
			{
				if (currentObject.TryGetProperty(propertyName, out var nextObject))
				{
					currentObject = nextObject;
				}
				else
				{
					return null;
				}
			}

			return currentObject.ToString();
		}

		private string[] GetEntryHeaders(JsonElement entry)
		{
			var headers = entry.GetProperty("request").GetProperty("headers");
			var headerStrings = new string[headers.GetArrayLength()];

			for (int i = 0; i < headers.GetArrayLength(); i++)
			{
				var header = headers[i];
				headerStrings[i] = $"{header.GetProperty("name").GetString()}: {header.GetProperty("value").GetString()}";
			}

			return headerStrings;
		}

		public static class Attribute
		{
			public static XAttribute Of(string attributeName, string attributeValue)
			{
				return new XAttribute(attributeName, attributeValue);
			}
		}

		public static class KeyValue
		{
			public static XElement Of(string elementName, string attributeName, string attributeValue)
			{
				return new XElement(elementName,
						Attribute.Of("name", attributeName),
						Attribute.Of("value", attributeValue)
				);
			}
		}

		private XElement CreateCookieManager(JsonElement entry)
		{
			var cookieManager = new XElement("CookieManager",
				new XAttribute("guiclass", "CookiePanel"),
				new XAttribute("testclass", "CookieManager"),
				new XAttribute("testname", "HTTP Cookie Manager"),
				new XAttribute("enabled", "true"),
				new XElement("collectionProp", new XAttribute("name", "CookieManager.cookies"))
			);

			if (entry.TryGetProperty("request", out var request) && request.TryGetProperty("cookies", out var cookies))
			{
				foreach (var cookie in cookies.EnumerateArray())
				{
					if (cookie.TryGetProperty("name", out var name) && cookie.TryGetProperty("value", out var value))
					{
						var cookieElement = new XElement("elementProp",
							new XAttribute("name", name.GetString()),
							new XAttribute("elementType", "Cookie"),
							new XAttribute("guiclass", "CookiePanel"),
							new XAttribute("testclass", "Cookie"),
							new XAttribute("testname", name.GetString()),
							new XAttribute("enabled", "true"),
							new XElement("stringProp", new XAttribute("name", "Cookie.name"), name.GetString()),
							new XElement("stringProp", new XAttribute("name", "Cookie.value"), value.GetString()),
							new XElement("stringProp", new XAttribute("name", "Cookie.domain"), ""),
							new XElement("stringProp", new XAttribute("name", "Cookie.path"), ""),
							new XElement("boolProp", new XAttribute("name", "Cookie.secure"), "false"),
							new XElement("longProp", new XAttribute("name", "Cookie.expires"), "0"),
							new XElement("boolProp", new XAttribute("name", "Cookie.path_spec"), "true"),
							new XElement("boolProp", new XAttribute("name", "Cookie.domain_spec"), "false")
						);

						cookieManager.Element("collectionProp").Add(cookieElement);
					}
				}
			}

			return cookieManager;
		}

		private XElement CreateHttpSampler(string url, string method, string postData, string[] headers)
		{
			var uri = new Uri(url);
			var port = uri.Port.ToString();
			var sampler = new XElement("HTTPSamplerProxy",
					Attribute.Of("guiclass", "HttpTestSampleGui"),
					Attribute.Of("testclass", "HTTPSamplerProxy"),
					Attribute.Of("testname", url),
					Attribute.Of("enabled", "true"),
					KeyValue.Of("elementProp", "HTTPsampler.Arguments", "Arguments"),
					KeyValue.Of("stringProp", "HTTPSampler.domain", url),
					KeyValue.Of("stringProp", "HTTPSampler.port", port),
					KeyValue.Of("stringProp", "HTTPSampler.protocol", "http"),
					KeyValue.Of("stringProp", "HTTPSampler.contentEncoding", ""),
					KeyValue.Of("stringProp", "HTTPSampler.path", ""),
					KeyValue.Of("stringProp", "HTTPSampler.method", method),
					KeyValue.Of("boolProp", "HTTPSampler.follow_redirects", "true"),
					KeyValue.Of("boolProp", "HTTPSampler.auto_redirects", "false"),
					KeyValue.Of("boolProp", "HTTPSampler.use_keepalive", "true"),
					KeyValue.Of("boolProp", "HTTPSampler.DO_MULTIPART_POST", "false"),
					KeyValue.Of("stringProp", "HTTPSampler.embedded_url_re", ""),
					KeyValue.Of("stringProp", "HTTPSampler.connect_timeout", ""),
					KeyValue.Of("stringProp", "HTTPSampler.response_timeout", ""),
					KeyValue.Of("stringProp", "HTTPSampler.implementation", "HttpClient4"),
					KeyValue.Of("boolProp", "HTTPSampler.monitor", "false"),
					KeyValue.Of("stringProp", "HTTPSampler.embedded_url_regex", "")
			);

			var cookieManager = CreateCookieManager(JsonDocument.Parse("{}").RootElement);
			sampler.Add(cookieManager);

			if (!string.IsNullOrEmpty(postData))
			{
				sampler.Element("elementProp").Element("collectionProp").Add(
						KeyValue.Of("elementProp", "", "HTTPArgument"),
						KeyValue.Of("boolProp", "HTTPArgument.always_encode", "false"),
						KeyValue.Of("stringProp", "Argument.value", postData),
						KeyValue.Of("stringProp", "Argument.metadata", "="),
						KeyValue.Of("boolProp", "HTTPArgument.use_equals", "true"),
						KeyValue.Of("stringProp", "Argument.name", "")
				);
			}

			if (headers.Length > 0)
			{
				sampler.Add(
						new XElement("collectionProp",
								Attribute.Of("name", "HTTPSampler.header_manager"),
								headers.Select(header =>
										new XElement("elementProp",
												Attribute.Of("name", ""),
												Attribute.Of("elementType", "Header"),
												KeyValue.Of("stringProp", "Header.name", header.Split(':')[0]),
												KeyValue.Of("stringProp", "Header.value", header.Split(':')[1].Trim())
										)
								)
						)
				);
			}

			return sampler;
		}

		private void AddSamplerToJmxDocument(XDocument jmxDocument, XElement sampler)
		{
			jmxDocument.Element("jmeterTestPlan").Element("hashTree").Add(
				new XElement("hashTree",
					sampler
				)
			);
		}
	}
}
