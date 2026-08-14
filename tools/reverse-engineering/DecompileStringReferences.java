// Find functions referencing an ASCII string and print their decompilation.
// @category OpenSynapse

import java.nio.charset.StandardCharsets;
import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;

public class DecompileStringReferences extends GhidraScript {
    @Override
    public void run() throws Exception {
        String needle = getScriptArgs().length == 0
            ? "sendFeatureReportInBatch"
            : getScriptArgs()[0];
        byte[] bytes = needle.getBytes(StandardCharsets.US_ASCII);
        Address hit = currentProgram.getMemory().findBytes(
            currentProgram.getMinAddress(), bytes, null, true, monitor);
        if (hit == null) {
            println("STRING_NOT_FOUND " + needle);
            return;
        }

        println("STRING " + needle + " " + hit);
        Set<Function> functions = new LinkedHashSet<>();
        collectReferences(hit, functions);
        for (int offset = 1; offset < bytes.length; offset++) {
            collectReferences(hit.add(offset), functions);
        }

        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            for (Function function : functions) {
                println("FUNCTION " + function.getName() + " " + function.getEntryPoint());
                DecompileResults result = decompiler.decompileFunction(function, 120, monitor);
                if (result.decompileCompleted()) {
                    println(result.getDecompiledFunction().getC());
                } else {
                    println("DECOMPILE_FAILED " + result.getErrorMessage());
                }
            }
        } finally {
            decompiler.dispose();
        }
    }

    private void collectReferences(Address address, Set<Function> functions) {
        for (Reference reference : getReferencesTo(address)) {
            Function function = getFunctionContaining(reference.getFromAddress());
            println("REFERENCE " + reference.getFromAddress() + " -> " + address);
            if (function != null) {
                functions.add(function);
            }
        }
    }
}
