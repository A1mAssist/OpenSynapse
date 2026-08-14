// Export MSVC RTTI/vftable symbols relevant to the lighting effect classes.
// @category OpenSynapse

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.Locale;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class ExportRttiSymbols extends GhidraScript {
    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) {
            throw new IllegalArgumentException("Usage: ExportRttiSymbols.java <output-file>");
        }
        File output = new File(args[0]);
        File parent = output.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
            throw new IllegalStateException("Cannot create output directory: " + parent);
        }
        try (PrintWriter writer = new PrintWriter(output, StandardCharsets.UTF_8)) {
            SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
            int count = 0;
            while (symbols.hasNext() && !monitor.isCancelled()) {
                Symbol symbol = symbols.next();
                String name = symbol.getName();
                String lower = name.toLowerCase(Locale.ROOT);
                if (lower.contains("effect") || lower.contains("lightingengine") ||
                    lower.contains("vftable") || lower.contains("typeinfo")) {
                    writer.printf("%s\t%s\t%s\t%s%n", symbol.getSymbolType(),
                        symbol.getAddress(), symbol.getName(), symbol.isPrimary());
                    count++;
                }
            }
            writer.flush();
            println("Exported " + count + " RTTI/effect symbols to " + output.getAbsolutePath());
        }
    }
}
