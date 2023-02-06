using RuleChecker;

RuleEngine engine = new RuleEngine();
engine.LoadRuleTypes();
engine.LoadJson(File.ReadAllText("C:\\Users\\AORUS\\Documents\\Coding\\RuleSystem\\test.json"));

//RuleTest test = engine.Test("C:\\Users\\AORUS\\Documents\\Coding\\RuleSystem\\Test");
RuleTest test = engine.Test("C:\\Users\\AORUS\\Documents\\Unity\\RPGProject\\Assets");

Console.WriteLine(test);
engine.ExportRuleTypes("C:\\Users\\AORUS\\Documents\\Coding\\RuleSystem\\out.rules");
test.ExportToFile("C:\\Users\\AORUS\\Documents\\Coding\\RuleSystem\\out.test");