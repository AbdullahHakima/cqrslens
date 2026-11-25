"""Exercise modeling, impact, TDD, and security through an installed CqrsLens executable."""

import json
from pathlib import Path
import subprocess
import sys
import tempfile


ROOT = Path(__file__).resolve().parents[1]
SAMPLE = ROOT / "samples" / "SampleShop"
REQUEST = "SampleShop.Application.Orders.CreateOrder.CreateOrderCommand"
HANDLER = "SampleShop.Application.Orders.CreateOrder.CreateOrderCommandHandler"
DESIGN_FILE = SAMPLE / "create-order.slice.json"
CONFLICTING_REFERENCES = [{"from": "SampleShop.Application", "to": "SampleShop.Infrastructure"}]
SCENARIO = {
    "name": "rejects an empty order",
    "given": "A valid customer and an empty list of order lines",
    "when": "CreateOrderCommand is validated",
    "then": "Validation reports an error for Lines",
}
EXPECTED_TOOLS = {
    "get_architecture_dna",
    "get_system_overview",
    "trace_slice",
    "trace_entity_impact",
    "check_architecture",
    "trace_request_impact",
    "verify_claim",
    "diff_architecture",
    "evaluate_architecture_proposal",
    "plan_slice_tests",
    "verify_slice_design",
    "draft_slice_design",
    "analyze_change_impact",
    "verify_tdd_cycle",
}
EXPECTED_PROMPTS = {"model_feature", "plan_refactor", "review_architecture_change"}


def run(tool, *arguments, expected_code=0, stdin=None):
    process = subprocess.run(
        [tool, *arguments], cwd=ROOT, input=stdin, capture_output=True,
        encoding="utf-8", timeout=60, check=False,
    )
    if process.returncode != expected_code:
        raise RuntimeError(
            f"{arguments}: expected exit {expected_code}, got {process.returncode}\n"
            f"stdout: {process.stdout}\nstderr: {process.stderr}"
        )
    if process.stderr:
        raise RuntimeError(f"Unexpected stderr from {arguments}: {process.stderr}")
    return process.stdout


def expect(condition, message):
    if not condition:
        raise RuntimeError(message)


def request(request_id, method, **params):
    return {"jsonrpc": "2.0", "id": request_id, "method": method, "params": params}


def tool_call(request_id, name, **arguments):
    return request(request_id, "tools/call", name=name, arguments=arguments)


def structured(response):
    expect(response["isError"] is False, f"MCP tool failed: {response}")
    body = response["structuredContent"]
    expect(json.loads(response["content"][0]["text"]) == body, "MCP response projections differ")
    return body


def text_result(response):
    expect(response["isError"] is False, f"MCP tool failed: {response}")
    return response["content"][0]["text"]


def verify_mcp(tool, design):
    messages = [
        request(1, "initialize", protocolVersion="2025-06-18", capabilities={},
                clientInfo={"name": "cqrslens-package-smoke", "version": "1"}),
        {"jsonrpc": "2.0", "method": "notifications/initialized"},
        request(2, "tools/list"),
        request(3, "prompts/list"),
        request(4, "prompts/get", name="model_feature", arguments={"feature": "Validate positive quantity"}),
        request(5, "prompts/get", name="plan_refactor", arguments={"target": "Order", "goal": "Extract pricing"}),
        request(6, "prompts/get", name="review_architecture_change", arguments={"base": "."}),
        tool_call(7, "get_architecture_dna", includeIndex=True),
        tool_call(8, "trace_slice", query="CreateOrder"),
        tool_call(9, "trace_entity_impact", entity="Order"),
        tool_call(10, "check_architecture", groupBy="project"),
        tool_call(11, "trace_request_impact", request="CreateOrderCommand"),
        tool_call(12, "verify_claim", subject="CreateOrderCommand", relation="has handler", object=HANDLER),
        tool_call(13, "diff_architecture", base="."),
        tool_call(14, "evaluate_architecture_proposal", references=CONFLICTING_REFERENCES),
        tool_call(15, "plan_slice_tests", request=REQUEST, scenarios=[SCENARIO]),
        tool_call(16, "verify_slice_design", design=design),
        tool_call(17, "draft_slice_design", request="CreateOrderCommand"),
        tool_call(18, "analyze_change_impact", target="Order", targetKind="entity"),
        tool_call(19, "verify_tdd_cycle", design=design),
        tool_call(20, "get_architecture_dna", path="/tmp"),
        tool_call(21, "verify_tdd_cycle", design=design, testResultsPath="/tmp/nonexistent.trx"),
        tool_call(22, "get_system_overview"),
        tool_call(23, "get_system_overview", service="SampleShop.Api"),
        tool_call(24, "get_system_overview", path="/tmp"),
    ]
    payload = "\n".join(json.dumps(message) for message in messages) + "\n"
    raw_lines = [json.loads(line) for line in run(tool, SAMPLE, "--mcp", stdin=payload).splitlines()]
    expect([message["id"] for message in raw_lines] == list(range(1, 25)), "Unexpected MCP response IDs")
    responses = {}
    for message in raw_lines:
        expect("result" in message, f"MCP request failed: {message}")
        responses[message["id"]] = message["result"]

    expect(responses[1]["protocolVersion"] == "2025-06-18", "Wrong MCP protocol revision")
    expect({"tools", "prompts"} <= responses[1]["capabilities"].keys(), "Missing MCP capabilities")

    listed_tools = responses[2]["tools"]
    expect(len(listed_tools) == 14, f"Expected 14 tools, got {len(listed_tools)}")
    expect(EXPECTED_TOOLS == {t["name"] for t in listed_tools}, "Unexpected MCP tool catalog")
    expect(all(t["annotations"]["readOnlyHint"] is True for t in listed_tools), "All MCP tools must be read-only")

    listed_prompts = {p["name"] for p in responses[3]["prompts"]}
    expect(EXPECTED_PROMPTS == listed_prompts, f"Unexpected MCP prompts: {listed_prompts}")
    expect("<feature_request>" in responses[4]["messages"][0]["content"]["text"], "model_feature delimiter missing")
    expect("<refactor_target>" in responses[5]["messages"][0]["content"]["text"], "plan_refactor delimiter missing")
    expect("<review_base>" in responses[6]["messages"][0]["content"]["text"], "review_architecture_change delimiter missing")

    expect("Architectural DNA" in text_result(responses[7]), "get_architecture_dna failed")
    expect("CreateOrderCommand" in text_result(responses[8]), "trace_slice failed")
    expect("Order" in text_result(responses[9]), "trace_entity_impact failed")
    expect("Architecture Checks" in text_result(responses[10]), "check_architecture failed")
    expect("CreateOrderCommandHandler" in text_result(responses[11]), "trace_request_impact failed")
    expect(text_result(responses[12]).startswith("verdict: supported"), "verify_claim failed")
    expect("Changed**: no" in text_result(responses[13]), "diff_architecture failed")

    proposal = structured(responses[14])
    expect(proposal["status"] == "conflicts", "Expected the SampleShop layer and cycle conflict")
    expect({"ApplicationLayerViolation", "CircularProjectDependency"} <=
           {finding["code"] for finding in proposal["findings"]}, "Missing dependency or cycle finding")

    plan = structured(responses[15])
    expect(plan["status"] == "existing" and plan["scenarios"] == [SCENARIO], "Wrong TDD plan or scenario")
    expect("validation" in {test["boundary"] for test in plan["suggestedTests"]}, "Missing validator test boundary")

    verification = structured(responses[16])
    draft = structured(responses[17])
    expect(draft["status"] == "drafted" and draft["request"] == REQUEST, "draft_slice_design failed")

    impact = structured(responses[18])
    expect(impact["status"] == "analyzed", "analyze_change_impact failed")
    expect(any(item["kind"] == "dbcontext" and item["fullName"] == "SampleShop.Infrastructure.Persistence.ShopDbContext"
               for item in impact["impactedItems"]),
           "analyze_change_impact missing ShopDbContext for Order entity")

    tdd = structured(responses[19])
    expect(tdd["designStatus"] == "matched" and tdd["phase"] == "green_incomplete",
           f"Unexpected verify_tdd_cycle result: {tdd}")

    expect(responses[20]["isError"] is True and "escapes allowed MCP workspace roots" in responses[20]["content"][0]["text"],
           f"Expected MCP path guard rejection for /tmp, got {responses[20]}")
    expect(responses[21]["isError"] is True and "escapes allowed MCP workspace roots" in responses[21]["content"][0]["text"],
           f"Expected MCP path guard rejection for testResultsPath=/tmp/..., got {responses[21]}")

    overview = text_result(responses[22])
    expect("SampleShop.Api" in overview and "SampleShop.Application" in overview,
           f"get_system_overview missing expected projects: {overview}")
    focused_overview = text_result(responses[23])
    expect("SampleShop.Api" in focused_overview,
           f"get_system_overview(service=SampleShop.Api) missing SampleShop.Api: {focused_overview}")
    expect(responses[24]["isError"] is True and "escapes allowed MCP workspace roots" in responses[24]["content"][0]["text"],
           f"Expected MCP path guard rejection for get_system_overview(path=/tmp), got {responses[24]}")

    return proposal, verification, draft, impact, tdd


def verify_cli(tool, proposal, verification, draft, impact, tdd):
    with tempfile.TemporaryDirectory(prefix="cqrslens-modeling-smoke-") as temp:
        proposal_path = Path(temp) / "proposal.json"
        proposal_path.write_text(json.dumps({"references": CONFLICTING_REFERENCES}), encoding="utf-8")
        expect(json.loads(run(tool, SAMPLE, "--proposal", proposal_path)) == proposal,
               "Architecture proposal results differ between CLI and MCP")

        trx_path = Path(temp) / "sample.trx"
        trx_path.write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            '<Results><UnitTestResult testName="SampleShop.Tests.CreateOrderTests.Passes" outcome="Passed" /></Results>'
            '</TestRun>',
            encoding="utf-8",
        )
        tdd_strict = json.loads(run(tool, SAMPLE, "--tdd-check", DESIGN_FILE, "--test-results", trx_path, "--strict", expected_code=4))
        expect(tdd_strict["phase"] == "green_incomplete", "SampleShop has no static test project, so --tdd-check --strict must fail closed")

    matched = json.loads(run(tool, SAMPLE, "--slice-design", DESIGN_FILE, "--strict"))
    expect(matched == verification, "Slice design verification differs between CLI and MCP")
    expect(matched["status"] == "matched" and matched["checks"], "SampleShop design did not match the scan")

    batch = json.loads(run(tool, SAMPLE, "--slice-design", SAMPLE, "--strict"))
    expect(batch["status"] == "matched" and batch["total"] == 1 and batch["matched"] == 1,
           f"Directory --slice-design batch failed: {batch}")

    cli_draft = json.loads(run(tool, SAMPLE, "--draft-slice", "CreateOrderCommand"))
    expect(cli_draft == draft, "Slice design draft differs between CLI and MCP")

    cli_impact = json.loads(run(tool, SAMPLE, "--impact", "Order", "--impact-kind", "entity"))
    expect(cli_impact == impact, "Change impact report differs between CLI and MCP")

    cli_tdd = json.loads(run(tool, SAMPLE, "--tdd-check", DESIGN_FILE))
    expect(cli_tdd == tdd, "TDD cycle evaluation differs between CLI and MCP")

    strict_failure = ROOT / "tests" / "fixtures" / "StrictFailure"
    rejected = json.loads(run(tool, strict_failure, "--slice-design", DESIGN_FILE, "--strict", expected_code=4))
    expect(rejected["status"] != "matched", "A nonmatching design passed the strict gate")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit("Usage: python3 scripts/smoke-modeling.py /path/to/installed/cqrslens")
    tool = str(Path(sys.argv[1]).resolve())
    design = json.loads(DESIGN_FILE.read_text(encoding="utf-8"))
    verify_cli(tool, *verify_mcp(tool, design))
    print("Installed CqrsLens modeling smoke passed")
