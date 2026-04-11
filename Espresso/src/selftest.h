#pragma once

/*
 * selftest.h -- built-in regression selftest
 *
 * Runs espresso on every PLA file in examples_dir, renders each minimized PLA
 * in every supported output format, computes a SHA-256 for each rendering, and
 * compares it against the expected hash stored in <examples_dir>/hash.txt.
 *
 * hash.txt format (one entry per line):
 *   <basename>|<format>  <sha256hex>
 *
 * Usage from the command line:
 *   espresso -selftest [dir]           -- validate against hash.txt
 *   espresso -selftest generate [dir]  -- compute and write hash.txt
 *
 * dir defaults to "tests" relative to the current working directory
 * when omitted.  hash.txt lives at <dir>/hash.txt and its keys are
 * paths relative to dir (e.g. "examples/al2").
 *
 * Return value: 0 on success, 1 on failure.
 */
int run_selftest(const char * test_dir);
int run_selftest_generate(const char *test_dir);
