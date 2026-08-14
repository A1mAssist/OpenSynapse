// Export effect-class vftable slots and resolved function boundaries.
// @category OpenSynapse

import java.io.File;
import java.io.PrintWriter;
import java.nio.charset.StandardCharsets;
import java.util.HashSet;
import java.util.Locale;
import java.util.Set;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Symbol;
import ghidra.program.model.symbol.SymbolIterator;

public class ExportEffectVftables extends GhidraScript {
    private static final String[] TARGETS = {
        "RippleEffect", "StarlightEffect", "SpectrumEffect", "FireEffect",
        "ReactiveEffect", "BreathingEffect", "WaveEffect"
    };

    @Override
    protected void run() throws Exception {
        String[] args = getScriptArgs();
        if (args.length < 1) {
            throw new IllegalArgumentException("Usage: ExportEffectVftables.java <output-file>");
        }
        File output = new File(args[0]);
        File parent = output.getParentFile();
        if (parent != null && !parent.isDirectory() && !parent.mkdirs()) {
            throw new IllegalStateException("Cannot create output directory: " + parent);
        }
        try (PrintWriter writer = new PrintWriter(output, StandardCharsets.UTF_8)) {
            SymbolIterator symbols = currentProgram.getSymbolTable().getAllSymbols(true);
            Set<String> emitted = new HashSet<>();
            while (symbols.hasNext() && !monitor.isCancelled()) {
                Symbol symbol = symbols.next();
                if (!symbol.getName().toLowerCase(Locale.ROOT).contains("vftable") ||
                    symbol.getName().toLowerCase(Locale.ROOT).contains("meta_ptr")) {
                    continue;
                }
                Address vf = symbol.getAddress();
                String type;
                try {
                    type = resolveType(vf);
                } catch (Exception ex) {
                    continue;
                }
                String matched = matchingTarget(type);
                if (matched == null || !emitted.add(vf.toString())) {
                    continue;
                }
                writer.printf("CLASS\t%s\tVFTABLE\t%s%n", matched, vf);
                for (int slot = 0; slot < 64; slot++) {
                    Address slotAddress = vf.add(slot * 8L);
                    long pointer = currentProgram.getMemory().getLong(slotAddress);
                    if (pointer == 0) {
                        break;
                    }
                    Address target = currentProgram.getAddressFactory().getDefaultAddressSpace()
                        .getAddress(pointer);
                    Function function = getFunctionAt(target);
                    if (function == null) {
                        function = getFunctionContaining(target);
                    }
                    if (function == null || function.isExternal()) {
                        break;
                    }
                    long size = function.getBody().getNumAddresses();
                    writer.printf("SLOT\t%d\t%s\t%s\t0x%x\t%s%n", slot, target,
                        function.getName(), size, function.getSignature().getPrototypeString());
                }
            }
        }
    }

    private String matchingTarget(String type) {
        for (String target : TARGETS) {
            if (type.contains(target)) {
                return target;
            }
        }
        return null;
    }

    private String resolveType(Address vf) throws Exception {
        Address meta = vf.subtract(8);
        long colValue = currentProgram.getMemory().getLong(meta);
        Address col = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(colValue);
        int typeRva = currentProgram.getMemory().getInt(col.add(0x0c));
        Address typeDesc = currentProgram.getImageBase().add(Integer.toUnsignedLong(typeRva));
        return readCString(typeDesc.add(0x10), 256);
    }

    private String readCString(Address address, int max) throws Exception {
        StringBuilder value = new StringBuilder();
        for (int i = 0; i < max; i++) {
            byte b = currentProgram.getMemory().getByte(address.add(i));
            if (b == 0) {
                break;
            }
            value.append((char) (b & 0xff));
        }
        return value.toString();
    }
}
