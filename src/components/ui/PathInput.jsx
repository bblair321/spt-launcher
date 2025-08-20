import React from "react";
import { FileText } from "lucide-react";

/**
 * Reusable path input component with file picker
 * @param {Object} props - Component props
 * @param {string} props.value - Current path value
 * @param {Function} props.onChange - Change handler
 * @param {Function} props.onSelectFile - File selection handler
 * @param {string} props.placeholder - Input placeholder
 * @param {string} props.label - Input label
 * @param {boolean} props.disabled - Whether input is disabled
 * @returns {JSX.Element} - Path input component
 */
function PathInput({
  value,
  onChange,
  onSelectFile,
  placeholder,
  label,
  disabled = false,
}) {
  return (
    <div className="space-y-2">
      {label && (
        <label className="block text-sm font-medium text-gray-700">
          {label}
        </label>
      )}
      <div className="flex space-x-2">
        <input
          type="text"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          disabled={disabled}
          className="flex-1 px-3 py-2 border border-gray-300 rounded-md bg-white text-gray-900 disabled:opacity-50 disabled:cursor-not-allowed"
        />
        <button
          onClick={onSelectFile}
          disabled={disabled}
          className="px-4 py-2 bg-gray-200 hover:bg-gray-300 text-gray-700 rounded-md transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          title="Select file"
        >
          <FileText className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}

export default PathInput;
