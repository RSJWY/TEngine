"""Deterministic tests for Pipeline response decoding and audited command policy."""

import json
from pathlib import Path
import sys
import unittest

SCRIPTS = Path(__file__).resolve().parents[1] / "scripts"
sys.path.insert(0, str(SCRIPTS))

from workflow_lib.core import WorkflowError  # noqa: E402
from workflow_lib.unity import (  # noqa: E402
    READ_ONLY, MUTATIONS, command_names, decode_response, parse_instances,
    test_listing, transport_failure, validate_parameters, validate_test_result,
)

PROJECT = r"E:\Unity\example"


def envelope(data):
    return json.dumps({"success": True, "command": "x", "data": data, "errors": [], "warnings": []})


class DecodeResponseTest(unittest.TestCase):
    def test_cli_and_pipeline_envelopes_are_unwrapped(self):
        payload = envelope({"command": "editor_status", "result": {"status": "ready"}})
        self.assertEqual(decode_response(payload), {"status": "ready"})

    def test_nested_json_string_is_unwrapped(self):
        inner = envelope({"status": "ready"})
        self.assertEqual(decode_response(json.dumps({"success": True, "data": inner})), {"status": "ready"})

    def test_business_failure_is_an_error_not_a_result(self):
        with self.assertRaises(WorkflowError):
            decode_response(json.dumps({"success": False, "data": {}, "errors": [{"code": "STATUS_NO_INSTANCES"}]}))

    def test_non_json_output_is_rejected(self):
        with self.assertRaises(WorkflowError):
            decode_response("not json")


class ValidateParametersTest(unittest.TestCase):
    def test_reserved_flags_and_dash_prefixed_keys_are_rejected(self):
        for parameters in ({"confirm": True}, {"--path": "x"}, {"project-path": "x"}, {"bad key": 1}):
            with self.assertRaises(WorkflowError):
                validate_parameters(parameters)

    def test_plain_snake_case_parameters_pass(self):
        self.assertEqual(validate_parameters({"mode": "editor"}), {"mode": "editor"})


class TransportFailureTest(unittest.TestCase):
    def test_play_mode_and_domain_reload_network_errors_are_retryable(self):
        payload = json.dumps({"success": False, "data": None, "errors": [
            {"code": "COMMAND_FAILED", "message": "Failed to execute command 'test_status': "
                                                  "Network error: An error occurred while sending the request."}]})
        self.assertTrue(transport_failure(payload))

    def test_no_instances_is_retryable(self):
        self.assertTrue(transport_failure(json.dumps({"errors": [{"code": "STATUS_NO_INSTANCES"}]})))

    def test_a_rejected_operation_is_not_mistaken_for_transport(self):
        payload = json.dumps({"success": False, "data": None, "errors": [
            {"code": "COMMAND_FAILED", "message": "Command is not registered: tengine_bake_ugui"}]})
        self.assertFalse(transport_failure(payload))

    def test_non_json_output_is_not_retryable(self):
        self.assertFalse(transport_failure("boom"))


class CommandPolicyTest(unittest.TestCase):
    def test_every_mutation_is_audited_and_not_read_only(self):
        for command in MUTATIONS:
            self.assertNotIn(command, READ_ONLY)

    def test_command_discovery_reads_the_list_payload(self):
        payload = json.dumps({"tools": [{"name": "a"}, {"name": "b"}]})
        self.assertEqual(command_names(json.loads(payload)), {"a", "b"})

    def test_command_discovery_rejects_unknown_schema(self):
        with self.assertRaises(WorkflowError):
            command_names({"unexpected": 1})


class ParseInstancesTest(unittest.TestCase):
    def ready(self, project=PROJECT, state="ready", count=1):
        instances = [{"project": project, "state": state, "port": 7800}] if count else []
        return json.dumps({"success": True, "data": {"count": len(instances), "instances": instances}})

    def test_single_ready_matching_instance_is_accepted(self):
        instance = parse_instances(self.ready(), PROJECT)
        self.assertEqual(instance["port"], 7800)

    def test_busy_editor_blocks_by_default(self):
        with self.assertRaises(WorkflowError) as error:
            parse_instances(self.ready(state="compiling"), PROJECT)
        self.assertEqual(error.exception.status, "blocked")

    def test_status_queries_may_observe_a_busy_editor(self):
        self.assertEqual(parse_instances(self.ready(state="compiling"), PROJECT, require_ready=False)["state"], "compiling")

    def test_zero_or_multiple_instances_block(self):
        with self.assertRaises(WorkflowError):
            parse_instances(self.ready(count=0), PROJECT)
        with self.assertRaises(WorkflowError):
            parse_instances(json.dumps({"success": True, "data": {"instances": [
                {"project": PROJECT, "state": "ready"}, {"project": PROJECT, "state": "ready"}]}}), PROJECT)

    def test_other_project_instance_is_rejected(self):
        with self.assertRaises(WorkflowError) as error:
            parse_instances(self.ready(project=r"E:\Unity\other"), PROJECT)
        self.assertIn("does not match", str(error.exception))


class TestSelectionTest(unittest.TestCase):
    listing = {"tests": [
        {"fullName": "A.One", "mode": "EditMode", "assembly": "TEngine.Workflow"},
        {"fullName": "A.Two", "mode": "PlayMode", "assembly": "TEngine.Workflow"},
        {"fullName": "B.Three", "mode": "EditMode", "assembly": "Other"},
        {"fullName": "C.Four", "mode": "EditMode", "assembly": "TEngine.Workflow", "explicit": True},
    ]}

    def test_mode_and_filter_narrow_the_selection(self):
        self.assertEqual(test_listing(self.listing, "editor", "TEngine.Workflow", "assembly"), {"A.One"})
        self.assertEqual(test_listing(self.listing, "playmode", "TEngine.Workflow", "assembly"), {"A.Two"})

    def test_explicit_tests_are_never_auto_selected(self):
        with self.assertRaises(WorkflowError) as error:
            test_listing(self.listing, "editor", "C.Four", "testName")
        self.assertEqual(error.exception.status, "blocked")

    def test_no_matching_test_blocks_instead_of_passing_vacuously(self):
        with self.assertRaises(WorkflowError) as error:
            test_listing(self.listing, "editor", "Nope", "assembly")
        self.assertEqual(error.exception.status, "blocked")

    def test_listing_without_identities_is_an_error(self):
        with self.assertRaises(WorkflowError):
            test_listing({"summary": {"total": 3}}, "editor", "x", "assembly")


class TestResultTest(unittest.TestCase):
    def result(self, status="passed", total=1):
        results = [{"fullName": "A.One", "status": status}] * total
        return {"status": "completed", "summary": {"total": total, "passed": total if status == "passed" else 0,
                                                  "failed": 0 if status == "passed" else total,
                                                  "skipped": 0, "inconclusive": 0}, "results": results}

    def test_consistent_result_is_accepted(self):
        self.assertEqual(validate_test_result(self.result(), {"A.One"})["total"], 1)

    def test_zero_executed_tests_cannot_pass(self):
        with self.assertRaises(WorkflowError):
            validate_test_result(self.result(total=0), set())

    def test_changed_identities_invalidate_a_retained_result(self):
        with self.assertRaises(WorkflowError):
            validate_test_result(self.result(), {"Other.Test"})

    def test_unfinished_run_is_an_error(self):
        value = self.result()
        value["status"] = "running"
        with self.assertRaises(WorkflowError):
            validate_test_result(value, {"A.One"})


if __name__ == "__main__":
    unittest.main()
