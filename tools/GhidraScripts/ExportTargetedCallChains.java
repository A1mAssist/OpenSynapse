// Export matching strings/symbols, their containing functions, callers, and decompilation.
// @category OpenSynapse

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Data;
import ghidra.program.model.listing.DataIterator;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;
import ghidra.program.model.symbol.ReferenceIterator;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class ExportTargetedCallChains extends GhidraScript {
    private record Match(String kind, String keyword, Address address, String value) {}

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 2) {
            throw new IllegalArgumentException(
                "Usage: ExportTargetedCallChains.java <output-file> <keyword> [keyword ...]");
        }

        File output = new File(args[0]);
        File parent = output.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
            throw new IllegalStateException("Cannot create output directory: " + parent);
        }

        List<String> keywords = new ArrayList<>();
        for (int i = 1; i < args.length; i++) {
            keywords.add(args[i].toLowerCase(Locale.ROOT));
        }

        List<Match> matches = findMatches(keywords);
        Map<Address, Function> targets = new LinkedHashMap<>();
        Map<Address, Set<Function>> callers = new LinkedHashMap<>();

        for (Match match : matches) {
            ReferenceIterator references = currentProgram.getReferenceManager()
                .getReferencesTo(match.address());
            while (references.hasNext()) {
                Reference reference = references.next();
                Function function = getFunctionContaining(reference.getFromAddress());
                if (function != null && !function.isExternal()) {
                    targets.putIfAbsent(function.getEntryPoint(), function);
                }
            }

            Function directFunction = getFunctionAt(match.address());
            if (directFunction != null && !directFunction.isExternal()) {
                targets.putIfAbsent(directFunction.getEntryPoint(), directFunction);
            }
        }

        for (Function target : targets.values()) {
            Set<Function> targetCallers = new LinkedHashSet<>();
            ReferenceIterator references = currentProgram.getReferenceManager()
                .getReferencesTo(target.getEntryPoint());
            while (references.hasNext()) {
                Function caller = getFunctionContaining(references.next().getFromAddress());
                if (caller != null && !caller.isExternal() && caller != target) {
                    targetCallers.add(caller);
                }
            }
            callers.put(target.getEntryPoint(), targetCallers);
        }

        try (PrintWriter writer = new PrintWriter(output, StandardCharsets.UTF_8)) {
            writer.println("# " + currentProgram.getName());
            writer.println();
            writer.println("Image base: `" + currentProgram.getImageBase() + "`");
            writer.println();
            writer.println("## Matches");
            for (Match match : matches) {
                writer.printf("- `%s` `%s` at `%s`: `%s`%n",
                    match.kind(), match.keyword(), match.address(), oneLine(match.value()));
            }

            DecompInterface decompiler = new DecompInterface();
            decompiler.openProgram(currentProgram);
            try {
                for (Function target : targets.values()) {
                    writer.println();
                    writer.printf("## %s at `%s`%n", target.getName(), target.getEntryPoint());
                    writer.println();
                    writer.println("Callers:");
                    Set<Function> targetCallers = callers.get(target.getEntryPoint());
                    if (targetCallers.isEmpty()) {
                        writer.println("- none resolved");
                    } else {
                        for (Function caller : targetCallers) {
                            writer.printf("- `%s` at `%s`%n", caller.getName(), caller.getEntryPoint());
                        }
                    }

                    writer.println();
                    writer.println("```c");
                    DecompileResults result = decompiler.decompileFunction(target, 60, monitor);
                    if (result.decompileCompleted()) {
                        writer.print(result.getDecompiledFunction().getC());
                    } else {
                        writer.println("/* Decompilation failed: " + result.getErrorMessage() + " */");
                    }
                    writer.println("```");
                }
            } finally {
                decompiler.dispose();
            }
        }

        println("Exported " + matches.size() + " matches and " + targets.size() +
            " functions to " + output.getAbsolutePath());
    }

    private List<Match> findMatches(List<String> keywords) {
        List<Match> matches = new ArrayList<>();
        Set<String> seen = new LinkedHashSet<>();

        DataIterator dataIterator = currentProgram.getListing().getDefinedData(true);
        while (dataIterator.hasNext() && !monitor.isCancelled()) {
            Data data = dataIterator.next();
            Object value = data.getValue();
            if (value instanceof String text) {
                addMatches(matches, seen, "string", data.getAddress(), text, keywords);
            }
        }

        SymbolIterator symbolIterator = currentProgram.getSymbolTable().getAllSymbols(true);
        while (symbolIterator.hasNext() && !monitor.isCancelled()) {
            Symbol symbol = symbolIterator.next();
            addMatches(matches, seen, "symbol", symbol.getAddress(), symbol.getName(), keywords);
        }

        return matches;
    }

    private static void addMatches(List<Match> matches, Set<String> seen, String kind,
            Address address, String value, List<String> keywords) {
        String normalized = value.toLowerCase(Locale.ROOT);
        for (String keyword : keywords) {
            if (normalized.contains(keyword)) {
                String key = kind + "|" + address + "|" + keyword;
                if (seen.add(key)) {
                    matches.add(new Match(kind, keyword, address, value));
                }
            }
        }
    }

    private static String oneLine(String value) {
        return value.replace('`', '\'').replace('\r', ' ').replace('\n', ' ');
    }
}
